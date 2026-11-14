using Microsoft.AspNetCore.Components;
using MigrateSQLServerDatabases.Models.Manager;
using MigrateSQLServerDatabases.Services.Manager;

namespace MigrateSQLServerDatabases.Components.Manager;

/// <summary>Shared run / script / close behaviour of the manager's dialogs.</summary>
public abstract class SqlDialogBase : LocalizedComponentBase
{
    [Inject] protected ManagerSession Session { get; set; } = default!;

    protected SqlRunState Run { get; } = new();
    protected string? Error { get; set; }

    /// <summary>Runs the T-SQL with live output; <paramref name="release"/> closes pooled connections to that database first.</summary>
    protected async Task<bool> RunSqlAsync(ManagedServer server, string? database, string sql, string? release = null)
    {
        Error = null;
        if (release is not null)
            Db.ClearPool(server.ConnectionStringFor(release));

        var ok = await Run.RunAsync(server.ConnectionStringFor(database ?? "master"), sql, action => InvokeAsync(action), StateHasChanged);

        if (release is not null)
            Db.ClearPool(server.ConnectionStringFor(release));
        return ok;
    }

    protected void ScriptToQuery(ManagedServer server, string? database, string sql)
    {
        Session.OpenQuery(server, database ?? "master", sql);
        Session.CloseDialog();
    }

    protected void Close()
    {
        if (!Run.IsRunning)
            Session.CloseDialog();
    }

    protected void Finish(string toastKey = "CommandsCompleted")
    {
        Session.Toast(L[toastKey]);
        Session.CloseDialog();
    }

    protected static string Mb(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
