using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>
/// The database manager's state for the current browser circuit: connected servers, the explorer tree,
/// open tabs, the open dialog, the context menu and notifications. Kept in memory only.
/// </summary>
public sealed class ManagerSession(ExplorerService explorer) : IAsyncDisposable
{
    private int queryNumber;

    public List<ExplorerNode> Roots { get; } = [];
    public List<WorkspaceTab> Tabs { get; } = [];
    public WorkspaceTab? ActiveTab { get; private set; }
    public ExplorerNode? Selected { get; private set; }
    public DialogRequest? Dialog { get; private set; }
    public ContextMenuState? Menu { get; private set; }
    public List<Toast> Toasts { get; } = [];

    /// <summary>The connect dialog opens by itself only the first time the page is shown.</summary>
    public bool ConnectPromptShown { get; set; }

    public event Action? Changed;

    public void Notify() => Changed?.Invoke();

    public IEnumerable<ManagedServer> Servers => Roots.Select(r => r.Server);

    public async Task<ExplorerNode> AddServerAsync(ManagedServer server)
    {
        var node = ExplorerService.ServerNode(server);
        Roots.Add(node);
        Selected = node;
        await ExpandAsync(node);
        if (node.Children?.FirstOrDefault(c => c.Kind is NodeKind.DatabasesFolder or NodeKind.SchemasFolder) is { } databases)
            await ExpandAsync(databases);
        return node;
    }

    public void Disconnect(ExplorerNode root)
    {
        Roots.Remove(root);
        if (Selected?.Server == root.Server)
            Selected = null;
        Notify();
    }

    public void Select(ExplorerNode node)
    {
        Selected = node;
        Notify();
    }

    public async Task ToggleAsync(ExplorerNode node)
    {
        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            Notify();
        }
        else
        {
            await ExpandAsync(node);
        }
    }

    public async Task ExpandAsync(ExplorerNode node, bool reload = false)
    {
        if (!node.CanExpand || node.IsLoading)
            return;

        node.IsExpanded = true;
        if (node.Children is not null && !reload)
        {
            Notify();
            return;
        }

        var previous = node.Children;
        node.IsLoading = true;
        node.Error = null;
        Notify();

        List<ExplorerNode>? children = null;
        try
        {
            children = await explorer.LoadChildrenAsync(node);
            node.Children = children;
        }
        catch (Exception ex)
        {
            node.Error = ex.Message;
            node.Children = null;
        }
        finally
        {
            node.IsLoading = false;
            Notify();
        }

        // Keep what was expanded open after a refresh
        if (children is not null && previous is not null)
        {
            var expanded = previous.Where(p => p.IsExpanded).Select(p => p.Key).ToHashSet();
            foreach (var child in children.Where(c => expanded.Contains(c.Key)))
                await ExpandAsync(child);
        }

        if (Selected is not null && previous?.Contains(Selected) == true)
            Selected = children?.FirstOrDefault(c => c.Key == Selected.Key) ?? node;
    }

    /// <summary>Reloads an expanded node; a collapsed one reloads when it is next expanded. Leaves refresh their parent.</summary>
    public async Task RefreshAsync(ExplorerNode? node)
    {
        if (node is null)
            return;
        if (!node.CanExpand)
        {
            await RefreshAsync(node.Parent);
            return;
        }

        if (node.IsExpanded)
            await ExpandAsync(node, reload: true);
        else
            node.Children = null;
        Notify();
    }

    /// <summary>Refreshes loaded folders of a kind, e.g. every Tables folder of one database after CREATE TABLE.</summary>
    public async Task RefreshWhereAsync(Func<ExplorerNode, bool> predicate)
    {
        foreach (var node in AllLoaded().Where(predicate).ToList())
            await RefreshAsync(node);
    }

    public IEnumerable<ExplorerNode> AllLoaded()
    {
        var stack = new Stack<ExplorerNode>(Roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            if (node.Children is not null)
                foreach (var child in node.Children)
                    stack.Push(child);
        }
    }

    public QueryTab OpenQuery(ManagedServer server, string? database, string sql = "", bool execute = false)
    {
        var tab = new QueryTab
        {
            Server = server,
            Database = database,
            Name = $"SQLQuery{++queryNumber}.sql",
            Sql = sql,
            ExecuteOnOpen = execute && !string.IsNullOrWhiteSpace(sql)
        };
        Open(tab);
        return tab;
    }

    public void Open(WorkspaceTab tab)
    {
        Tabs.Add(tab);
        ActiveTab = tab;
        Menu = null;
        Notify();
    }

    /// <summary>Activates an existing tab that matches, or opens a new one.</summary>
    public void OpenOrActivate<T>(Func<T, bool> match, Func<T> create) where T : WorkspaceTab
    {
        if (Tabs.OfType<T>().FirstOrDefault(match) is { } existing)
            Activate(existing);
        else
            Open(create());
    }

    public void Activate(WorkspaceTab tab)
    {
        ActiveTab = tab;
        Notify();
    }

    public async Task CloseTabAsync(WorkspaceTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        Tabs.RemoveAt(index);
        if (ActiveTab == tab)
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        Notify();
        await tab.DisposeAsync();
    }

    public void ShowDialog(Type component, IDictionary<string, object> parameters)
    {
        Menu = null;
        Dialog = new DialogRequest(component, parameters);
        Notify();
    }

    public void CloseDialog()
    {
        Dialog = null;
        Notify();
    }

    public void ShowMenu(double x, double y, IReadOnlyList<MenuItem> items)
    {
        Menu = items.Count == 0 ? null : new ContextMenuState(x, y, items);
        Notify();
    }

    public void CloseMenu()
    {
        if (Menu is null)
            return;
        Menu = null;
        Notify();
    }

    public void Toast(string text, ToastKind kind = ToastKind.Success)
    {
        Toasts.Add(new Toast(text, kind));
        if (Toasts.Count > 4)
            Toasts.RemoveAt(0);
        Notify();
    }

    public void Dismiss(Toast toast)
    {
        if (Toasts.Remove(toast))
            Notify();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in Tabs)
            await tab.DisposeAsync();
        Tabs.Clear();
    }
}

/// <summary>Runs a dialog's T-SQL on a new connection and shows the server's messages while it runs.</summary>
public sealed class SqlRunState
{
    private CancellationTokenSource? cancellation;

    public bool IsRunning { get; private set; }
    public QueryExecution? Execution { get; private set; }

    public bool Succeeded => Execution is { Completed: true, HasErrors: false, Cancelled: false };

    /// <param name="database">SQL Server: null means master. Other engines: null means the connection's own database.</param>
    /// <param name="dispatch">ComponentBase.InvokeAsync, to update the dialog from SqlClient threads.</param>
    public async Task<bool> RunAsync(ManagedServer server, string? database, string sql, Func<Action, Task> dispatch, Action changed)
    {
        cancellation = new CancellationTokenSource();
        var execution = new QueryExecution();
        Execution = execution;
        IsRunning = true;
        changed();

        try
        {
            var options = new RunOptions
            {
                OnBackgroundMessage = () => _ = dispatch(() =>
                {
                    execution.DrainPending();
                    changed();
                }),
                OnProgress = async () =>
                {
                    changed();
                    await Task.Yield();
                }
            };

            if (server.IsSqlServer)
            {
                await using var connection = await Db.OpenAsync(server.ConnectionStringFor(database ?? "master"), cancellation.Token);
                await QueryRunner.RunAsync(connection, sql, execution, options, cancellation.Token);
            }
            else
            {
                var dialect = Dialects.For(server);
                await using var connection = await dialect.OpenAsync(server, database, cancellation.Token);
                await GenericQueryRunner.RunAsync(dialect, connection, sql, execution, options, cancellation.Token);
            }
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            execution.AddError(ex.Message);
        }
        catch (Exception)
        {
            execution.Cancelled = true;
        }
        finally
        {
            execution.Completed = true;
            IsRunning = false;
            cancellation.Dispose();
            cancellation = null;
            changed();
        }

        return Succeeded;
    }

    public void Cancel()
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Reset() => Execution = null;
}
