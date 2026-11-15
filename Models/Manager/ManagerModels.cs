using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Localization;
using MigrateSQLServerDatabases.Services.Manager;

namespace MigrateSQLServerDatabases.Models.Manager;

public enum DatabaseEngine
{
    SqlServer,
    MySql,
    PostgreSql,
    Oracle
}

/// <summary>A server connected in the Object Explorer.</summary>
public sealed class ManagedServer(string displayName, string connectionString, ServerInfo info, string login, bool isSysAdmin,
    DatabaseEngine engine = DatabaseEngine.SqlServer)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public DatabaseEngine Engine { get; } = engine;
    public bool IsSqlServer => Engine == DatabaseEngine.SqlServer;

    /// <summary>The server name as typed (e.g. (localdb)\MSSQLLocalDB); @@SERVERNAME is in <see cref="Info"/>.</summary>
    public string DisplayName { get; } = displayName;
    public string ConnectionString { get; } = connectionString;
    public ServerInfo Info { get; } = info;
    public string Login { get; } = login;
    public bool IsSysAdmin { get; } = isSysAdmin;

    public int Major => Info.MajorVersion;
    public bool IsAzureDb => Info.EngineEdition == 5;

    /// <summary>Express (4) and Azure SQL Database (5) have no SQL Server Agent.</summary>
    public bool HasAgent => Info.EngineEdition is not (4 or 5);

    /// <summary>ALTER ROLE ... ADD MEMBER and ALTER SERVER ROLE exist from SQL Server 2012.</summary>
    public bool SupportsAlterRole => Major >= 11;

    public string ConnectionStringFor(string? database)
    {
        if (!IsSqlServer)
        {
            // MySQL and PostgreSQL open a database per connection; Oracle switches schema in the session instead
            if (string.IsNullOrEmpty(database) || Engine == DatabaseEngine.Oracle)
                return ConnectionString;
            var generic = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = ConnectionString };
            generic["Database"] = database;
            return generic.ConnectionString;
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;
        return builder.ConnectionString;
    }

    public string Master => ConnectionStringFor("master");
}

public enum NodeKind
{
    Server,
    DatabasesFolder, SystemDatabasesFolder, Database,
    TablesFolder, Table, ViewsFolder, View, SynonymsFolder, Synonym,
    ColumnsFolder, Column, KeysFolder, Key, ConstraintsFolder, Constraint, TriggersFolder, Trigger, IndexesFolder, Index,
    ProgrammabilityFolder, ProceduresFolder, Procedure, FunctionsFolder, Function, Parameter,
    SequencesFolder, Sequence, TypesFolder, UserType,
    DbSecurityFolder, UsersFolder, User, RolesFolder, Role, SchemasFolder, Schema,
    ServerSecurityFolder, LoginsFolder, Login, ServerRolesFolder, ServerRole,
    AgentFolder, JobsFolder, Job,
    // MySQL, PostgreSQL and Oracle
    SystemSchemasFolder, PackagesFolder, Package
}

/// <summary>A node in the Object Explorer tree. Children load the first time the node is expanded.</summary>
public sealed class ExplorerNode
{
    private string? key;

    public required NodeKind Kind { get; init; }
    public required ManagedServer Server { get; init; }
    public ExplorerNode? Parent { get; init; }

    /// <summary>Folders show a translated label; objects show their own name.</summary>
    public string? LabelKey { get; init; }
    public string Label { get; init; } = string.Empty;

    /// <summary>Extra info after the label, in SQL terms (e.g. "PK, int, not null").</summary>
    public string? Detail { get; init; }

    public string? Database { get; init; }
    public string? Schema { get; init; }
    public string? Name { get; init; }

    /// <summary>Owning table, view, procedure or function for columns, indexes, keys, triggers and parameters.</summary>
    public string? ParentSchema { get; init; }
    public string? ParentName { get; init; }
    public NodeKind? ParentKind { get; init; }

    /// <summary>Kind-specific type: PK/UQ/F for keys, C/D for constraints, FN/IF/TF for functions, S/U/G for principals.</summary>
    public string? SubType { get; init; }

    /// <summary>Database state (ONLINE, OFFLINE, RESTORING…).</summary>
    public string? State { get; init; }
    public long? RowCount { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsSystem { get; init; }

    public bool CanExpand { get; init; }
    public bool IsExpanded { get; set; }
    public bool IsLoading { get; set; }
    public string? Error { get; set; }
    public List<ExplorerNode>? Children { get; set; }

    public bool IsFolder => Kind.ToString().EndsWith("Folder", StringComparison.Ordinal);

    public bool IsOnline => State is null or "ONLINE";

    public string Key => key ??= Parent is null ? Server.Id : $"{Parent.Key}/{Kind}:{Schema}.{Name}";

    public string QualifiedName => Server.IsSqlServer ? Sql.Name(Schema, Name) : Dialects.For(Server.Engine).Name(Schema, Name);

    public string ParentQualifiedName => Server.IsSqlServer ? Sql.Name(ParentSchema, ParentName) : Dialects.For(Server.Engine).Name(ParentSchema, ParentName);

    /// <summary>"schema.name" for display.</summary>
    public string DisplayName => Schema is null ? Name ?? Label : $"{Schema}.{Name}";

    public ExplorerNode? Ancestor(NodeKind kind)
    {
        for (var node = Parent; node is not null; node = node.Parent)
            if (node.Kind == kind)
                return node;
        return null;
    }
}

public abstract class WorkspaceTab : IAsyncDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required ManagedServer Server { get; init; }
    public string? Database { get; set; }

    public abstract string Title(Localizer l);

    public virtual string Icon => "query";

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class QueryTab : WorkspaceTab
{
    public required string Name { get; init; }
    public string Sql { get; set; } = string.Empty;
    public string? FileName { get; set; }

    /// <summary>Run the script as soon as the tab opens (Select Top 1000 Rows, Execute Procedure…).</summary>
    public bool ExecuteOnOpen { get; set; }

    /// <summary>Kept open like an SSMS query window, so USE, temp tables and open transactions carry over between runs.</summary>
    public System.Data.Common.DbConnection? Connection { get; set; }
    public int? SessionId { get; set; }
    public string? ConnectionError { get; set; }

    public QueryExecution? Execution { get; set; }
    public bool IsRunning { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
    public bool ShowMessages { get; set; }

    public override string Title(Localizer l) => FileName ?? Name;

    public override async ValueTask DisposeAsync()
    {
        try
        {
            Cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (Connection is not null)
            await Connection.DisposeAsync();
        Connection = null;
    }
}

public sealed class TableDesignTab : WorkspaceTab
{
    /// <summary>Null for a new table.</summary>
    public string? Schema { get; set; }
    public string? Name { get; set; }

    /// <summary>Designer state, kept here so unsaved edits survive switching pages.</summary>
    public object? State { get; set; }

    public override string Title(Localizer l) =>
        Name is null ? l["NewTableTitle"] : $"{Schema}.{Name} · {l["DesignSuffix"]}";

    public override string Icon => "table";
}

public sealed class EditDataTab : WorkspaceTab
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public object? State { get; set; }

    public override string Title(Localizer l) => $"{Schema}.{Name} · {l["EditSuffix"]}";

    public override string Icon => "edit";
}

public sealed class DatabasePropertiesTab : WorkspaceTab
{
    public override string Title(Localizer l) => $"{Database} · {l["PropertiesSuffix"]}";

    public override string Icon => "database";
}

public sealed class ServerPropertiesTab : WorkspaceTab
{
    public override string Title(Localizer l) => $"{Server.DisplayName} · {l["PropertiesSuffix"]}";

    public override string Icon => "server";
}

public sealed class ActivityMonitorTab : WorkspaceTab
{
    public override string Title(Localizer l) => $"{l["ActivityMonitor"]} · {Server.DisplayName}";

    public override string Icon => "activity";
}

public enum ToastKind
{
    Success,
    Error,
    Info
}

public sealed record Toast(string Text, ToastKind Kind)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>A modal dialog shown by the manager page through DynamicComponent.</summary>
public sealed record DialogRequest(Type Component, IDictionary<string, object> Parameters)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}

public sealed class MenuItem
{
    public string? LabelKey { get; init; }
    public Func<Task>? Action { get; init; }
    public List<MenuItem>? Children { get; init; }
    public bool Danger { get; init; }
    public bool IsSeparator { get; init; }
    public bool Disabled { get; init; }
    public string? Shortcut { get; init; }

    public static MenuItem Separator => new() { IsSeparator = true };

    public static MenuItem Of(string labelKey, Func<Task> action, bool danger = false) =>
        new() { LabelKey = labelKey, Action = action, Danger = danger };

    public static MenuItem Of(string labelKey, Action action, bool danger = false) =>
        new() { LabelKey = labelKey, Action = () => { action(); return Task.CompletedTask; }, Danger = danger };

    public static MenuItem Sub(string labelKey, params MenuItem[] children) =>
        new() { LabelKey = labelKey, Children = [.. children] };
}

public sealed record ContextMenuState(double X, double Y, IReadOnlyList<MenuItem> Items)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}

public enum FileBrowserMode
{
    Open,
    Save,
    Folder
}

public enum FieldType
{
    Text,
    Password,
    Number,
    Select,
    Checkbox,
    TextArea
}

public sealed class ActionField
{
    public required string Key { get; init; }
    public required string LabelKey { get; init; }
    public FieldType Type { get; init; } = FieldType.Text;
    public string Value { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public IReadOnlyList<(string Value, string Label)> Options { get; init; } = [];
    public bool Required { get; init; }
    public string? HintKey { get; init; }
}

/// <summary>
/// A confirm-and-run dialog: optional input fields, a live preview of the T-SQL it will run,
/// "Script to a new query" and "Execute".
/// </summary>
public sealed class ActionRequest
{
    public required string TitleKey { get; init; }

    /// <summary>The object the action is about, shown under the title.</summary>
    public string? Subject { get; init; }
    public string? MessageKey { get; init; }
    public required ManagedServer Server { get; init; }

    /// <summary>Database the T-SQL runs in; null = master.</summary>
    public string? Database { get; init; }
    public List<ActionField> Fields { get; init; } = [];

    /// <summary>Builds the T-SQL. The flag is true for the preview, where passwords are masked.</summary>
    public required Func<ActionRequest, bool, string> BuildSql { get; init; }

    /// <summary>Returns a translation key when the input is invalid.</summary>
    public Func<ActionRequest, string?>? Validate { get; init; }
    public bool Danger { get; init; }
    public string ConfirmKey { get; init; } = "Execute";

    /// <summary>Keep the dialog open after success to show the server's output (DBCC, statistics…).</summary>
    public bool KeepOpen { get; init; }

    /// <summary>Show the T-SQL preview expanded.</summary>
    public bool ShowSql { get; init; }

    /// <summary>Database whose pooled connections must be closed first (drop, detach, offline, restore…).</summary>
    public string? ReleaseDatabase { get; init; }
    public ExplorerNode? Refresh { get; init; }
    public Func<Task>? OnSuccess { get; init; }

    public string Text(string key) => Fields.First(f => f.Key == key).Value.Trim();

    public bool Flag(string key) => Fields.First(f => f.Key == key).Checked;
}
