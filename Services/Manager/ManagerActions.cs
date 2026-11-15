using Microsoft.AspNetCore.Components;
using MigrateSQLServerDatabases.Components.Manager;
using MigrateSQLServerDatabases.Localization;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>Object Explorer context menus and the commands behind them.</summary>
public sealed class ManagerActions(
    ManagerSession session,
    SchemaService schema,
    MigrationSession migration,
    SqlServerService sqlServer,
    TableCopyJob copyJob,
    NavigationManager navigation,
    Localizer l)
{
    private static MenuItem Sep => MenuItem.Separator;

    public List<MenuItem> MenuFor(ExplorerNode n)
    {
        if (!n.Server.IsSqlServer)
            return OtherEngineMenu(n);

        List<MenuItem> items = n.Kind switch
        {
            NodeKind.Server => ServerMenu(n),
            NodeKind.DatabasesFolder => [Item("NewDatabase", () => NewDatabase(n.Server)), Item("AttachDatabase", () => Attach(n.Server)), Item("RestoreDatabase", () => Restore(n.Server, null))],
            NodeKind.Database => DatabaseMenu(n),
            NodeKind.TablesFolder => [Item("NewTable", () => NewTable(n))],
            NodeKind.Table => TableMenu(n),
            NodeKind.ColumnsFolder when n.ParentKind == NodeKind.Table => [Item("NewColumn", () => Design(n.Server, n.Database!, n.ParentSchema!, n.ParentName!))],
            NodeKind.Column when n.ParentKind == NodeKind.Table => [Item("Rename", () => Rename(n)), Item("Delete", () => Delete(n), danger: true)],
            NodeKind.KeysFolder => [Item("NewForeignKey", () => Design(n.Server, n.Database!, n.ParentSchema!, n.ParentName!))],
            NodeKind.Key => ConstraintMenu(n),
            NodeKind.ConstraintsFolder => [Item("NewCheckConstraint", () => NewCheck(n))],
            NodeKind.Constraint => ConstraintMenu(n),
            NodeKind.TriggersFolder when n.ParentKind == NodeKind.Table => [Item("NewTrigger", () => Template(n, ScriptBuilder.NewTriggerTemplate(n.ParentQualifiedName, n.ParentName!)))],
            NodeKind.Trigger => TriggerMenu(n),
            NodeKind.IndexesFolder => IndexesFolderMenu(n),
            NodeKind.Index => IndexMenu(n),
            NodeKind.ViewsFolder => [Item("NewView", () => Template(n, ScriptBuilder.NewViewTemplate()))],
            NodeKind.View => ViewMenu(n),
            NodeKind.SynonymsFolder => [Item("NewSynonym", () => NewSynonym(n))],
            NodeKind.Synonym => [ScriptMenu(n, "CREATE", "DROP"), Sep, Item("Delete", () => Delete(n), danger: true)],
            NodeKind.ProceduresFolder => [Item("NewProcedure", () => Template(n, ScriptBuilder.NewProcedureTemplate()))],
            NodeKind.Procedure => ProcedureMenu(n),
            NodeKind.FunctionsFolder => [MenuItem.Sub("New",
                Item("NewScalarFunction", () => Template(n, ScriptBuilder.NewScalarFunctionTemplate())),
                Item("NewInlineFunction", () => Template(n, ScriptBuilder.NewInlineFunctionTemplate())),
                Item("NewTableFunction", () => Template(n, ScriptBuilder.NewTableFunctionTemplate())))],
            NodeKind.Function => FunctionMenu(n),
            NodeKind.SequencesFolder => [Item("NewSequence", () => NewSequence(n))],
            NodeKind.Sequence or NodeKind.UserType => [ScriptMenu(n, "CREATE", "DROP"), Sep, Item("Delete", () => Delete(n), danger: true)],
            NodeKind.TypesFolder => [Item("NewType", () => Template(n, ScriptBuilder.NewTypeTemplate()))],
            NodeKind.UsersFolder => [Item("NewUser", () => ShowDialog<UserDialog>(n, null))],
            NodeKind.User => UserMenu(n),
            NodeKind.RolesFolder => [Item("NewRole", () => ShowDialog<RoleDialog>(n, null))],
            NodeKind.Role => RoleMenu(n),
            NodeKind.SchemasFolder => [Item("NewSchema", () => NewSchema(n))],
            NodeKind.Schema => [ScriptMenu(n, "CREATE", "DROP"), Sep, Item("Delete", () => Delete(n), danger: true)],
            NodeKind.LoginsFolder => [Item("NewLogin", () => Login(n.Server, null, n))],
            NodeKind.Login => LoginMenu(n),
            NodeKind.ServerRole => [Item("ViewMembers", () => session.OpenQuery(n.Server, "master", $"""
                SELECT m.name AS member_name, m.type_desc
                FROM sys.server_role_members rm
                JOIN sys.server_principals r ON r.principal_id = rm.role_principal_id
                JOIN sys.server_principals m ON m.principal_id = rm.member_principal_id
                WHERE r.name = {Sql.Lit(n.Name)}
                ORDER BY m.name;
                """, execute: true))],
            NodeKind.Job => JobMenu(n),
            _ => []
        };

        if (n.CanExpand || n.IsFolder)
        {
            if (items.Count > 0)
                items.Add(Sep);
            items.Add(Item("Refresh", () => session.RefreshAsync(n)));
        }

        return items;
    }

    // ---------- MySQL / PostgreSQL / Oracle ----------

    /// <summary>Menus for the other engines: queries, scripting, rename, delete, new database / schema, sessions and settings.</summary>
    private List<MenuItem> OtherEngineMenu(ExplorerNode n)
    {
        var dialect = Dialects.For(n.Server);
        var s = n.Server;
        // The database a query on this node opens in: MySQL / PostgreSQL database, Oracle schema
        var database = s.Engine == DatabaseEngine.Oracle ? n.Schema ?? n.ParentSchema : n.Database;
        var items = new List<MenuItem>();

        void Script(string labelKey, Func<Task<string>> build) => items.Add(Item(labelKey, async () =>
        {
            try
            {
                session.OpenQuery(s, database, await build());
            }
            catch (Exception ex)
            {
                session.Toast(ex.Message, ToastKind.Error);
            }
        }));

        void Template(string labelKey, string sql) => items.Add(Item(labelKey, () => session.OpenQuery(s, database, sql)));

        switch (n.Kind)
        {
            case NodeKind.Server:
                items.Add(Item("NewQuery", () => session.OpenQuery(s, null)));
                items.Add(Item("ActivityMonitor", () => session.OpenQuery(s, null, dialect.ActivitySql, execute: true)));
                items.Add(Item("Properties", () => session.OpenQuery(s, null, dialect.SettingsSql, execute: true)));
                if (dialect.CreateDatabaseSql("x") is not null)
                    items.Add(Item("NewDatabase", () => NewOtherDatabase(n.Children?.FirstOrDefault(c => c.Kind == NodeKind.DatabasesFolder) ?? n)));
                items.Add(Sep);
                items.Add(Item("Disconnect", () => session.Disconnect(n)));
                break;

            case NodeKind.DatabasesFolder when dialect.CreateDatabaseSql("x") is not null:
                items.Add(Item("NewDatabase", () => NewOtherDatabase(n)));
                break;

            case NodeKind.Database:
                items.Add(Item("NewQuery", () => session.OpenQuery(s, n.Name)));
                if (s.Engine == DatabaseEngine.MySql)
                    Template("NewTable", dialect.NewTableTemplate(n.Name));
                Script("ScriptCreate", () => dialect.ScriptCreateAsync(n));
                items.Add(Sep);
                AddRenameAndDelete();
                break;

            case NodeKind.SchemasFolder when dialect.CreateSchemaSql("x") is not null:
                items.Add(Item("NewSchema", () => NewOtherSchema(n)));
                break;

            case NodeKind.Schema:
                items.Add(Item("NewQuery", () => session.OpenQuery(s, database)));
                Template("NewTable", dialect.NewTableTemplate(n.Name));
                if (!n.IsSystem)
                {
                    items.Add(Sep);
                    AddRenameAndDelete();
                }

                break;

            case NodeKind.TablesFolder:
                Template("NewTable", dialect.NewTableTemplate(n.Schema));
                break;

            case NodeKind.ViewsFolder:
                Template("NewView", dialect.NewViewTemplate(n.Schema));
                break;

            case NodeKind.ProceduresFolder:
                Template("NewProcedure", dialect.NewProcedureTemplate(n.Schema));
                break;

            case NodeKind.FunctionsFolder:
                Template("NewScalarFunction", dialect.NewFunctionTemplate(n.Schema));
                break;

            case NodeKind.Table or NodeKind.View:
            {
                var table = n.QualifiedName;
                items.Add(Item("SelectTop1000", () => SelectTopOtherAsync(n, 1000)));
                if (n.Kind == NodeKind.Table)
                    items.Add(Item("EditTop200", () => SelectTopOtherAsync(n, 200)));
                items.Add(Sep);
                var scripts = new List<MenuItem>
                {
                    Item("ScriptCreate", () => ScriptOtherAsync(n, database, () => dialect.ScriptCreateAsync(n))),
                    Item("ScriptDrop", () => session.OpenQuery(s, database, dialect.DropSql(n) ?? "")),
                    Sep,
                    Item("ScriptSelect", () => ScriptOtherAsync(n, database, async () => dialect.SelectTop(table, await ColumnNamesAsync(n), 1000)))
                };
                if (n.Kind == NodeKind.Table)
                {
                    scripts.Add(Item("ScriptInsert", () => ScriptOtherAsync(n, database, async () => InsertTemplate(dialect, table, await ColumnsAsync(n)))));
                    scripts.Add(Item("ScriptUpdate", () => ScriptOtherAsync(n, database, async () => UpdateTemplate(dialect, table, await ColumnsAsync(n)))));
                    scripts.Add(Item("ScriptDelete", () => session.OpenQuery(s, database, $"DELETE FROM {table}\n WHERE <condition>;")));
                }

                items.Add(MenuItem.Sub(n.Kind == NodeKind.Table ? "ScriptTableAs" : "ScriptViewAs", [.. scripts]));
                items.Add(Sep);
                if (n.Kind == NodeKind.Table)
                    items.Add(Item("Truncate", () => ShowOther(n, "Truncate", dialect.TruncateSql(n), danger: true, confirm: "Truncate", message: "TruncateHint")));
                AddRenameAndDelete();
                break;
            }

            case NodeKind.Procedure or NodeKind.Function or NodeKind.Package or NodeKind.Trigger or NodeKind.Sequence or NodeKind.Synonym
                or NodeKind.Index or NodeKind.Key:
                Script("ScriptCreate", () => dialect.ScriptCreateAsync(n));
                items.Add(Sep);
                AddRenameAndDelete();
                break;

            case NodeKind.Column when n.ParentKind == NodeKind.Table:
            case NodeKind.Login or NodeKind.Role:
                AddRenameAndDelete();
                break;
        }

        if (n.CanExpand || n.IsFolder)
        {
            items.Add(Sep);
            items.Add(Item("Refresh", () => session.RefreshAsync(n)));
        }

        return items;

        void AddRenameAndDelete()
        {
            if (n.Kind != NodeKind.Database || s.Engine == DatabaseEngine.PostgreSql)
            {
                if (dialect.RenameSql(n, "x") is not null)
                    items.Add(Item("Rename", () => RenameOther(n)));
            }

            if (dialect.DropSql(n) is { } drop)
                items.Add(Item("Delete", () => ShowOther(n, n.Kind == NodeKind.Database ? "DeleteDatabase" : "DeleteObject", drop,
                    danger: true, confirm: "Delete", message: n.Kind == NodeKind.Database ? "DeleteDatabaseHint" : "ConfirmDeleteObject",
                    refresh: n.Parent, runIn: n.Kind is NodeKind.Database ? ServerDatabase(n) : database), danger: true));
        }
    }

    /// <summary>PostgreSQL can't drop or rename the database it's connected to, so those run in another one.</summary>
    private static string? ServerDatabase(ExplorerNode n) =>
        n.Server.Engine == DatabaseEngine.PostgreSql ? (n.Name == "postgres" ? "template1" : "postgres") : null;

    private void ShowOther(ExplorerNode n, string titleKey, string sql, bool danger = false, string confirm = "Execute",
        string? message = null, ExplorerNode? refresh = null, string? runIn = null) => ShowAction(new ActionRequest
    {
        TitleKey = titleKey,
        Subject = n.DisplayName,
        MessageKey = message,
        Server = n.Server,
        Database = runIn ?? (n.Server.Engine == DatabaseEngine.Oracle ? null : n.Database),
        BuildSql = (_, _) => sql,
        Danger = danger,
        ConfirmKey = confirm,
        ShowSql = true,
        Refresh = refresh ?? n.Parent
    });

    private void RenameOther(ExplorerNode n)
    {
        var dialect = Dialects.For(n.Server);
        var old = n.Name!;
        ShowAction(new ActionRequest
        {
            TitleKey = "Rename",
            Subject = n.DisplayName,
            Server = n.Server,
            Database = n.Kind == NodeKind.Database ? ServerDatabase(n) : n.Server.Engine == DatabaseEngine.Oracle ? null : n.Database,
            Fields = [new ActionField { Key = "name", LabelKey = "NewName", Value = old, Required = true }],
            Validate = r => r.Text("name") == old ? "NameUnchanged" : null,
            BuildSql = (r, _) => dialect.RenameSql(n, r.Text("name")) ?? string.Empty,
            Refresh = n.Parent
        });
    }

    private void NewOtherDatabase(ExplorerNode folder)
    {
        var dialect = Dialects.For(folder.Server);
        ShowAction(new ActionRequest
        {
            TitleKey = "NewDatabase",
            Subject = folder.Server.DisplayName,
            Server = folder.Server,
            Database = folder.Server.Engine == DatabaseEngine.PostgreSql ? "postgres" : null,
            Fields = [new ActionField { Key = "name", LabelKey = "DatabaseName", Required = true }],
            BuildSql = (r, _) => dialect.CreateDatabaseSql(r.Text("name")) ?? string.Empty,
            ConfirmKey = "Create",
            ShowSql = true,
            Refresh = folder.Kind == NodeKind.DatabasesFolder ? folder : null
        });
    }

    private void NewOtherSchema(ExplorerNode folder)
    {
        var dialect = Dialects.For(folder.Server);
        ShowAction(new ActionRequest
        {
            TitleKey = "NewSchema",
            Subject = folder.Database,
            Server = folder.Server,
            Database = folder.Database,
            Fields = [new ActionField { Key = "name", LabelKey = "Name", Required = true }],
            BuildSql = (r, _) => dialect.CreateSchemaSql(r.Text("name")) ?? string.Empty,
            ConfirmKey = "Create",
            ShowSql = true,
            Refresh = folder
        });
    }

    private async Task<List<ColumnKeysInfo>> ColumnsAsync(ExplorerNode n) =>
        await Dialects.For(n.Server).LoadColumnsAsync(n.Server, n.Database, n.Schema, n.Name!);

    private async Task<List<string>> ColumnNamesAsync(ExplorerNode n) => (await ColumnsAsync(n)).Select(c => c.Name).ToList();

    /// <summary>Select Top N; the results can be edited in the grid (by primary key).</summary>
    private async Task SelectTopOtherAsync(ExplorerNode n, int top)
    {
        try
        {
            var dialect = Dialects.For(n.Server);
            var columns = await ColumnNamesAsync(n);
            var sql = dialect.SelectTop(n.QualifiedName, columns.Count > 0 ? columns : ["*"], top);
            if (columns.Count == 0)
                sql = sql.Replace("\"*\"", "*").Replace("`*`", "*");
            var database = n.Server.Engine == DatabaseEngine.Oracle ? n.Schema : n.Database;
            session.OpenQuery(n.Server, database, sql, execute: true);
        }
        catch (Exception ex)
        {
            session.Toast(ex.Message, ToastKind.Error);
        }
    }

    private async Task ScriptOtherAsync(ExplorerNode n, string? database, Func<Task<string>> build)
    {
        try
        {
            session.OpenQuery(n.Server, database, await build());
        }
        catch (Exception ex)
        {
            session.Toast(ex.Message, ToastKind.Error);
        }
    }

    private static string InsertTemplate(EngineDialect dialect, string table, List<ColumnKeysInfo> columns)
    {
        var insertable = columns.Where(c => !c.Identity && !c.Computed).ToList();
        return $"INSERT INTO {table}\n           ({string.Join("\n           ,", insertable.Select(c => dialect.Q(c.Name)))})\n     VALUES\n           ({string.Join("\n           ,", insertable.Select(c => $"<{c.Name}, {c.Type}>"))});";
    }

    private static string UpdateTemplate(EngineDialect dialect, string table, List<ColumnKeysInfo> columns)
    {
        var updatable = columns.Where(c => !c.Identity && !c.Computed && !c.PrimaryKey).ToList();
        var keys = columns.Where(c => c.PrimaryKey).Select(c => $"{dialect.Q(c.Name)} = <{c.Name}>").ToList();
        return $"UPDATE {table}\n   SET {string.Join("\n      ,", updatable.Select(c => $"{dialect.Q(c.Name)} = <{c.Name}, {c.Type}>"))}\n WHERE {(keys.Count > 0 ? string.Join(" AND ", keys) : "<condition>")};";
    }

    private static MenuItem Item(string key, Func<Task> action, bool danger = false) => MenuItem.Of(key, action, danger);

    private static MenuItem Item(string key, Action action, bool danger = false) => MenuItem.Of(key, action, danger);

    // ---------- Menus ----------

    private List<MenuItem> ServerMenu(ExplorerNode n) =>
    [
        Item("NewQuery", () => session.OpenQuery(n.Server, null)),
        Item("ActivityMonitor", () => ActivityMonitor(n.Server)),
        Sep,
        Item("NewDatabase", () => NewDatabase(n.Server)),
        Item("RestoreDatabase", () => Restore(n.Server, null)),
        Item("AttachDatabase", () => Attach(n.Server)),
        Sep,
        Item("Properties", () => session.OpenOrActivate<ServerPropertiesTab>(t => t.Server == n.Server, () => new ServerPropertiesTab { Server = n.Server })),
        Item("Disconnect", () => session.Disconnect(n))
    ];

    private List<MenuItem> DatabaseMenu(ExplorerNode n)
    {
        var db = n.Name!;
        var online = n.IsOnline;
        var items = new List<MenuItem>();
        if (online)
            items.Add(Item("NewQuery", () => session.OpenQuery(n.Server, db)));

        var tasks = new List<MenuItem>();
        if (online)
        {
            tasks.Add(Item("BackUp", () => ShowDialog<BackupDialog>(n.Server, db)));
            tasks.Add(Item("RestoreDatabase", () => Restore(n.Server, db)));
            tasks.Add(Sep);
            tasks.Add(Item("Shrink", () => Shrink(n)));
            tasks.Add(Item("CheckIntegrity", () => Run(n, "CheckIntegrity", $"DBCC CHECKDB ({Sql.Lit(db)}) WITH ALL_ERRORMSGS;", database: "master", keepOpen: true)));
            tasks.Add(Item("UpdateStatistics", () => Run(n, "UpdateStatistics", "EXEC sp_updatestats;", database: db, keepOpen: true)));
            tasks.Add(Item("GenerateScripts", () => ShowDialog<GenerateScriptsDialog>(n.Server, db)));
            tasks.Add(Sep);
        }
        else
        {
            tasks.Add(Item("RestoreDatabase", () => Restore(n.Server, db)));
        }

        if (!n.IsSystem)
        {
            if (n.State == "OFFLINE")
                tasks.Add(Item("BringOnline", () => Run(n, "BringOnline", $"ALTER DATABASE {Sql.Q(db)} SET ONLINE;", database: "master", release: db, refresh: n.Parent)));
            else if (online)
                tasks.Add(Item("TakeOffline", () => Run(n, "TakeOffline", $"ALTER DATABASE {Sql.Q(db)} SET OFFLINE WITH ROLLBACK IMMEDIATE;", database: "master", release: db, refresh: n.Parent, danger: true, messageKey: "TakeOfflineHint")));
            tasks.Add(Item("Detach", () => Detach(n)));
        }

        items.Add(MenuItem.Sub("Tasks", [.. tasks]));
        if (online)
            items.Add(MenuItem.Sub("UseForMigration",
                Item("AsMigrationSource", () => UseForMigrationAsync(n, source: true)),
                Item("AsMigrationDestination", () => UseForMigrationAsync(n, source: false))));

        if (!n.IsSystem)
        {
            items.Add(Sep);
            items.Add(Item("Rename", () => Rename(n)));
            items.Add(Item("Delete", () => DeleteDatabase(n), danger: true));
        }

        items.Add(Sep);
        items.Add(Item("Properties", () => DatabaseProperties(n.Server, db)));
        return items;
    }

    private List<MenuItem> TableMenu(ExplorerNode n)
    {
        var db = n.Database!;
        return
        [
            Item("SelectTop1000", () => SelectTopAsync(n)),
            Item("EditTop200", () => session.OpenOrActivate<EditDataTab>(
                t => t.Server == n.Server && t.Database == db && t.Schema == n.Schema && t.Name == n.Name,
                () => new EditDataTab { Server = n.Server, Database = db, Schema = n.Schema!, Name = n.Name! })),
            Item("Design", () => Design(n.Server, db, n.Schema!, n.Name!)),
            Sep,
            MenuItem.Sub("ScriptTableAs",
                Item("ScriptCreate", () => ScriptAsync(n, "CREATE")),
                Item("ScriptDropAndCreate", () => ScriptAsync(n, "DROPCREATE")),
                Item("ScriptDrop", () => ScriptAsync(n, "DROP")),
                Sep,
                Item("ScriptSelect", () => ScriptAsync(n, "SELECT")),
                Item("ScriptInsert", () => ScriptAsync(n, "INSERT")),
                Item("ScriptUpdate", () => ScriptAsync(n, "UPDATE")),
                Item("ScriptDelete", () => ScriptAsync(n, "DELETE"))),
            Item("NewIndex", () => NewIndex(n.Server, db, n.Schema!, n.Name!)),
            Item("NewTrigger", () => Template(n, ScriptBuilder.NewTriggerTemplate(n.QualifiedName, n.Name!))),
            Sep,
            Item("Rename", () => Rename(n)),
            Item("Truncate", () => Truncate(n), danger: true),
            Item("Delete", () => Delete(n), danger: true)
        ];
    }

    private List<MenuItem> ViewMenu(ExplorerNode n) =>
    [
        Item("SelectTop1000", () => SelectTopAsync(n)),
        Item("Modify", () => ScriptAsync(n, "ALTER")),
        Sep,
        MenuItem.Sub("ScriptViewAs",
            Item("ScriptCreate", () => ScriptAsync(n, "CREATE")),
            Item("ScriptAlter", () => ScriptAsync(n, "ALTER")),
            Item("ScriptDropAndCreate", () => ScriptAsync(n, "DROPCREATE")),
            Item("ScriptDrop", () => ScriptAsync(n, "DROP")),
            Sep,
            Item("ScriptSelect", () => ScriptAsync(n, "SELECT"))),
        Sep,
        Item("Rename", () => Rename(n)),
        Item("Delete", () => Delete(n), danger: true)
    ];

    private List<MenuItem> ProcedureMenu(ExplorerNode n) =>
    [
        Item("ExecuteProcedure", () => ShowDialog<ExecuteProcedureDialog>(n, null)),
        Item("Modify", () => ScriptAsync(n, "ALTER")),
        Sep,
        MenuItem.Sub("ScriptProcedureAs",
            Item("ScriptCreate", () => ScriptAsync(n, "CREATE")),
            Item("ScriptAlter", () => ScriptAsync(n, "ALTER")),
            Item("ScriptDropAndCreate", () => ScriptAsync(n, "DROPCREATE")),
            Item("ScriptDrop", () => ScriptAsync(n, "DROP")),
            Sep,
            Item("ScriptExecute", () => ScriptAsync(n, "EXECUTE"))),
        Sep,
        Item("Rename", () => Rename(n)),
        Item("Delete", () => Delete(n), danger: true)
    ];

    private List<MenuItem> FunctionMenu(ExplorerNode n) =>
    [
        Item("Modify", () => ScriptAsync(n, "ALTER")),
        Sep,
        MenuItem.Sub("ScriptFunctionAs",
            Item("ScriptCreate", () => ScriptAsync(n, "CREATE")),
            Item("ScriptAlter", () => ScriptAsync(n, "ALTER")),
            Item("ScriptDropAndCreate", () => ScriptAsync(n, "DROPCREATE")),
            Item("ScriptDrop", () => ScriptAsync(n, "DROP")),
            Sep,
            Item("ScriptSelect", () => ScriptAsync(n, "SELECT"))),
        Sep,
        Item("Rename", () => Rename(n)),
        Item("Delete", () => Delete(n), danger: true)
    ];

    private List<MenuItem> TriggerMenu(ExplorerNode n)
    {
        var trigger = Sql.Q(n.ParentSchema!, n.Name!);
        return
        [
            Item("Modify", () => ScriptAsync(n, "ALTER")),
            MenuItem.Sub("ScriptTriggerAs",
                Item("ScriptCreate", () => ScriptAsync(n, "CREATE")),
                Item("ScriptAlter", () => ScriptAsync(n, "ALTER")),
                Item("ScriptDrop", () => ScriptAsync(n, "DROP"))),
            Sep,
            n.IsDisabled
                ? Item("Enable", () => Run(n, "Enable", $"ENABLE TRIGGER {trigger} ON {n.ParentQualifiedName};", refresh: n.Parent))
                : Item("Disable", () => Run(n, "Disable", $"DISABLE TRIGGER {trigger} ON {n.ParentQualifiedName};", refresh: n.Parent)),
            Item("Delete", () => Delete(n), danger: true)
        ];
    }

    private List<MenuItem> IndexesFolderMenu(ExplorerNode n)
    {
        var items = new List<MenuItem>();
        if (n.ParentKind == NodeKind.Table)
            items.Add(Item("NewIndex", () => NewIndex(n.Server, n.Database!, n.ParentSchema!, n.ParentName!)));
        items.Add(Item("RebuildAll", () => Run(n, "RebuildAll", $"ALTER INDEX ALL ON {n.ParentQualifiedName} REBUILD;", refresh: n)));
        items.Add(Item("ReorganizeAll", () => Run(n, "ReorganizeAll", $"ALTER INDEX ALL ON {n.ParentQualifiedName} REORGANIZE;", refresh: n)));
        return items;
    }

    private List<MenuItem> IndexMenu(ExplorerNode n)
    {
        var index = $"{Sql.Q(n.Name)} ON {n.ParentQualifiedName}";
        var items = new List<MenuItem>
        {
            ScriptMenu(n, "CREATE", "DROP", "DROPCREATE"),
            Sep,
            Item(n.IsDisabled ? "Enable" : "Rebuild", () => Run(n, n.IsDisabled ? "Enable" : "Rebuild", $"ALTER INDEX {index} REBUILD;", refresh: n.Parent))
        };
        if (!n.IsDisabled)
        {
            items.Add(Item("Reorganize", () => Run(n, "Reorganize", $"ALTER INDEX {index} REORGANIZE;")));
            items.Add(Item("Disable", () => Run(n, "Disable", $"ALTER INDEX {index} DISABLE;", refresh: n.Parent, danger: true,
                messageKey: n.Detail?.Contains("Clustered", StringComparison.Ordinal) == true && !n.Detail.Contains("Non-Clustered", StringComparison.OrdinalIgnoreCase) ? "DisableClusteredHint" : null)));
        }

        items.Add(Sep);
        items.Add(Item("Rename", () => Rename(n)));
        items.Add(Item("Delete", () => Delete(n), danger: true));
        return items;
    }

    private List<MenuItem> ConstraintMenu(ExplorerNode n)
    {
        var items = new List<MenuItem> { ScriptMenu(n, "CREATE", "DROP") };
        if (n.SubType is "F" or "C")
        {
            items.Add(Sep);
            items.Add(n.IsDisabled
                ? Item("Enable", () => Run(n, "Enable", $"ALTER TABLE {n.ParentQualifiedName} WITH CHECK CHECK CONSTRAINT {Sql.Q(n.Name)};", refresh: n.Parent))
                : Item("Disable", () => Run(n, "Disable", $"ALTER TABLE {n.ParentQualifiedName} NOCHECK CONSTRAINT {Sql.Q(n.Name)};", refresh: n.Parent)));
        }

        items.Add(Sep);
        items.Add(Item("Rename", () => Rename(n)));
        items.Add(Item("Delete", () => Delete(n), danger: true));
        return items;
    }

    private List<MenuItem> UserMenu(ExplorerNode n)
    {
        var items = new List<MenuItem>();
        if (!n.IsSystem)
            items.Add(Item("Properties", () => ShowDialog<UserDialog>(n, n.Name)));
        items.Add(ScriptMenu(n, "CREATE", "DROP"));
        if (!n.IsSystem)
        {
            items.Add(Sep);
            items.Add(Item("Rename", () => Rename(n)));
            items.Add(Item("Delete", () => Delete(n), danger: true));
        }

        return items;
    }

    private List<MenuItem> RoleMenu(ExplorerNode n)
    {
        var items = new List<MenuItem>();
        if (n.SubType == "R" && n.Name != "public")
            items.Add(Item("Properties", () => ShowDialog<RoleDialog>(n, n.Name)));
        if (!n.IsSystem)
        {
            items.Add(ScriptMenu(n, "CREATE", "DROP"));
            items.Add(Sep);
            items.Add(Item("Rename", () => Rename(n)));
            items.Add(Item("Delete", () => Delete(n), danger: true));
        }

        return items;
    }

    private List<MenuItem> LoginMenu(ExplorerNode n)
    {
        var login = Sql.Q(n.Name);
        return
        [
            Item("Properties", () => Login(n.Server, n.Name, n.Parent)),
            n.IsDisabled
                ? Item("Enable", () => Run(n, "Enable", $"ALTER LOGIN {login} ENABLE;", database: "master", refresh: n.Parent))
                : Item("Disable", () => Run(n, "Disable", $"ALTER LOGIN {login} DISABLE;", database: "master", refresh: n.Parent)),
            ScriptMenu(n, "CREATE", "DROP"),
            Sep,
            Item("Rename", () => Rename(n)),
            Item("Delete", () => Delete(n), danger: true)
        ];
    }

    private List<MenuItem> JobMenu(ExplorerNode n)
    {
        var job = Sql.Lit(n.Name);
        return
        [
            Item("StartJob", () => Run(n, "StartJob", $"EXEC msdb.dbo.sp_start_job @job_name = {job};", database: "msdb")),
            Item("StopJob", () => Run(n, "StopJob", $"EXEC msdb.dbo.sp_stop_job @job_name = {job};", database: "msdb")),
            n.IsDisabled
                ? Item("Enable", () => Run(n, "Enable", $"EXEC msdb.dbo.sp_update_job @job_name = {job}, @enabled = 1;", database: "msdb", refresh: n.Parent))
                : Item("Disable", () => Run(n, "Disable", $"EXEC msdb.dbo.sp_update_job @job_name = {job}, @enabled = 0;", database: "msdb", refresh: n.Parent)),
            Item("ViewHistory", () => session.OpenQuery(n.Server, "msdb", $"""
                SELECT TOP (200)
                       msdb.dbo.agent_datetime(h.run_date, h.run_time) AS run_at,
                       h.step_id, h.step_name,
                       CASE h.run_status WHEN 0 THEN 'Failed' WHEN 1 THEN 'Succeeded' WHEN 2 THEN 'Retry' WHEN 3 THEN 'Canceled' WHEN 4 THEN 'In progress' END AS status,
                       STUFF(STUFF(RIGHT('000000' + CAST(h.run_duration AS varchar(10)), 6), 5, 0, ':'), 3, 0, ':') AS duration,
                       h.message
                FROM msdb.dbo.sysjobhistory h
                JOIN msdb.dbo.sysjobs j ON j.job_id = h.job_id
                WHERE j.name = {job}
                ORDER BY h.instance_id DESC;
                """, execute: true)),
            Sep,
            Item("Delete", () => Delete(n), danger: true)
        ];
    }

    private MenuItem ScriptMenu(ExplorerNode n, params string[] kinds) =>
        MenuItem.Sub("ScriptAs", kinds.Select(k => Item(k switch
        {
            "CREATE" => "ScriptCreate",
            "ALTER" => "ScriptAlter",
            "DROP" => "ScriptDrop",
            _ => "ScriptDropAndCreate"
        }, () => ScriptAsync(n, k))).ToArray());

    // ---------- Commands ----------

    public void NewDatabase(ManagedServer server) => session.ShowDialog(typeof(CreateDatabaseDialog), new Dictionary<string, object> { ["Server"] = server });

    public void Attach(ManagedServer server) => session.ShowDialog(typeof(AttachDialog), new Dictionary<string, object> { ["Server"] = server });

    public void Restore(ManagedServer server, string? database) =>
        session.ShowDialog(typeof(RestoreDialog), new Dictionary<string, object> { ["Server"] = server, ["Database"] = database ?? string.Empty });

    public void ActivityMonitor(ManagedServer server) =>
        session.OpenOrActivate<ActivityMonitorTab>(t => t.Server == server, () => new ActivityMonitorTab { Server = server });

    public void DatabaseProperties(ManagedServer server, string database) =>
        session.OpenOrActivate<DatabasePropertiesTab>(t => t.Server == server && t.Database == database,
            () => new DatabasePropertiesTab { Server = server, Database = database });

    public void Login(ManagedServer server, string? login, ExplorerNode? refresh)
    {
        var parameters = new Dictionary<string, object> { ["Server"] = server };
        if (login is not null)
            parameters["LoginName"] = login;
        session.ShowDialog(typeof(LoginDialog), parameters);
    }

    private void ShowDialog<T>(ManagedServer server, string database) where T : IComponent =>
        session.ShowDialog(typeof(T), new Dictionary<string, object> { ["Server"] = server, ["Database"] = database });

    private void ShowDialog<T>(ExplorerNode node, string? name) where T : IComponent
    {
        var parameters = new Dictionary<string, object> { ["Node"] = node };
        if (name is not null)
            parameters["Name"] = name;
        session.ShowDialog(typeof(T), parameters);
    }

    public void NewTable(ExplorerNode folder) =>
        session.Open(new TableDesignTab { Server = folder.Server, Database = folder.Database });

    public void Design(ManagedServer server, string database, string schemaName, string table) =>
        session.OpenOrActivate<TableDesignTab>(
            t => t.Server == server && t.Database == database && t.Schema == schemaName && t.Name == table,
            () => new TableDesignTab { Server = server, Database = database, Schema = schemaName, Name = table });

    public void NewIndex(ManagedServer server, string database, string schemaName, string table) =>
        session.ShowDialog(typeof(IndexDialog), new Dictionary<string, object>
        {
            ["Server"] = server, ["Database"] = database, ["Schema"] = schemaName, ["Table"] = table
        });

    private void Template(ExplorerNode n, string sql) => session.OpenQuery(n.Server, n.Database, sql);

    private async Task SelectTopAsync(ExplorerNode n)
    {
        try
        {
            var columns = await schema.GetColumnNamesAsync(n.Server, n.Database!, n.QualifiedName);
            session.OpenQuery(n.Server, n.Database,
                "/****** Script for SelectTopNRows command ******/\n" + ScriptBuilder.SelectTop(n.Database!, n.QualifiedName, columns, 1000),
                execute: true);
        }
        catch (Exception ex)
        {
            session.Toast(ex.Message, ToastKind.Error);
        }
    }

    private async Task ScriptAsync(ExplorerNode n, string kind)
    {
        try
        {
            session.OpenQuery(n.Server, n.Database ?? "master", await BuildScriptAsync(n, kind));
        }
        catch (Exception ex)
        {
            session.Toast(ex.Message, ToastKind.Error);
        }
    }

    private async Task<string> BuildScriptAsync(ExplorerNode n, string kind)
    {
        var header = $"/****** Object:  {n.Kind} {(n.Kind is NodeKind.Index or NodeKind.Key or NodeKind.Constraint or NodeKind.Trigger ? Sql.Q(n.Name) : n.QualifiedName)}    Script Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ******/\n";
        switch (kind)
        {
            case "CREATE":
                return header + await schema.ScriptCreateAsync(n);
            case "ALTER":
                return header + SqlText.ToAlter(await schema.ScriptCreateAsync(n));
            case "DROP":
                return header + DropSql(n) + "\nGO\n";
            case "DROPCREATE":
                return header + GuardedDrop(n) + "\nGO\n\n" + await schema.ScriptCreateAsync(n);
            case "SELECT" when n.Kind == NodeKind.Function:
            {
                var parameters = await schema.GetParametersAsync(n.Server, n.Database!, n.Schema!, n.Name!);
                var args = string.Join(", ", parameters.Where(p => p.Id > 0).Select(p => $"<{p.Name}, {p.TypeSql},>"));
                return n.SubType is "FN" or "FS" ? $"SELECT {n.QualifiedName}({args});\n" : $"SELECT *\n  FROM {n.QualifiedName}({args});\n";
            }
            case "SELECT":
            {
                var columns = await schema.GetColumnNamesAsync(n.Server, n.Database!, n.QualifiedName);
                return $"SELECT {string.Join("\n      ,", columns.Select(Sql.Q))}\n  FROM {n.QualifiedName};\n";
            }
            case "EXECUTE":
            {
                var parameters = await schema.GetParametersAsync(n.Server, n.Database!, n.Schema!, n.Name!);
                return ScriptBuilder.ExecuteProcedure(n.QualifiedName, parameters, parameters.ToDictionary(p => p.Name, p => (string?)$"<{p.Name}, {p.TypeSql},>"))
                    .Replace("N'<", "<").Replace(",>'", ",>");
            }
            case "INSERT" or "UPDATE":
            {
                var table = await schema.GetTableAsync(n.Server, n.Database!, n.Schema!, n.Name!);
                return kind == "INSERT" ? ScriptBuilder.InsertTemplate(table.FullName, table.Columns) : ScriptBuilder.UpdateTemplate(table.FullName, table.Columns);
            }
            case "DELETE":
                return ScriptBuilder.DeleteTemplate(n.QualifiedName);
            default:
                throw new NotSupportedException(kind);
        }
    }

    private static string GuardedDrop(ExplorerNode n) => n.Kind switch
    {
        NodeKind.Table => ScriptBuilder.DropTable(n.QualifiedName),
        NodeKind.View or NodeKind.Procedure or NodeKind.Function => $"IF OBJECT_ID({Sql.Lit(n.QualifiedName)}) IS NOT NULL\n    {DropSql(n)}",
        NodeKind.Index => $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID({Sql.Lit(n.ParentQualifiedName)}) AND name = {Sql.Lit(n.Name)})\n    {DropSql(n)}",
        _ => DropSql(n)
    };

    public static string DropSql(ExplorerNode n)
    {
        var name = Sql.Q(n.Name);
        return n.Kind switch
        {
            NodeKind.Table => $"DROP TABLE {n.QualifiedName};",
            NodeKind.View => $"DROP VIEW {n.QualifiedName};",
            NodeKind.Procedure => $"DROP PROCEDURE {n.QualifiedName};",
            NodeKind.Function => $"DROP FUNCTION {n.QualifiedName};",
            NodeKind.Synonym => $"DROP SYNONYM {n.QualifiedName};",
            NodeKind.Sequence => $"DROP SEQUENCE {n.QualifiedName};",
            NodeKind.UserType => $"DROP TYPE {n.QualifiedName};",
            NodeKind.Trigger => $"DROP TRIGGER {Sql.Q(n.ParentSchema!, n.Name!)};",
            NodeKind.Index when n.SubType is "PK" or "UQ" => $"ALTER TABLE {n.ParentQualifiedName} DROP CONSTRAINT {name};",
            NodeKind.Index => $"DROP INDEX {name} ON {n.ParentQualifiedName};",
            NodeKind.Key or NodeKind.Constraint => $"ALTER TABLE {n.ParentQualifiedName} DROP CONSTRAINT {name};",
            NodeKind.Column => $"""
                DECLARE @default sysname = (SELECT d.name FROM sys.default_constraints d
                    JOIN sys.columns c ON c.default_object_id = d.object_id
                    WHERE c.object_id = OBJECT_ID({Sql.Lit(n.ParentQualifiedName)}) AND c.name = {Sql.Lit(n.Name)});
                IF @default IS NOT NULL
                    EXEC (N'ALTER TABLE {n.ParentQualifiedName.Replace("'", "''")} DROP CONSTRAINT ' + QUOTENAME(@default));
                ALTER TABLE {n.ParentQualifiedName} DROP COLUMN {name};
                """,
            NodeKind.User => $"DROP USER {name};",
            NodeKind.Role => $"DROP ROLE {name};",
            NodeKind.Schema => $"DROP SCHEMA {name};",
            NodeKind.Login => $"DROP LOGIN {name};",
            NodeKind.Job => $"EXEC msdb.dbo.sp_delete_job @job_name = {Sql.Lit(n.Name)}, @delete_unused_schedule = 1;",
            _ => throw new NotSupportedException(n.Kind.ToString())
        };
    }

    /// <summary>Opens the confirm-and-run dialog for a fixed statement.</summary>
    public void Run(ExplorerNode n, string titleKey, string sql, string? database = null, ExplorerNode? refresh = null,
        bool danger = false, string? messageKey = null, string? release = null, bool keepOpen = false, string confirmKey = "Execute") =>
        ShowAction(new ActionRequest
        {
            TitleKey = titleKey,
            Subject = n.Kind == NodeKind.Database ? n.Name : n.DisplayName,
            MessageKey = messageKey,
            Server = n.Server,
            Database = database ?? n.Database,
            BuildSql = (_, _) => sql,
            Danger = danger,
            ConfirmKey = confirmKey,
            ReleaseDatabase = release,
            Refresh = refresh,
            KeepOpen = keepOpen,
            ShowSql = true
        });

    public void ShowAction(ActionRequest request) =>
        session.ShowDialog(typeof(ActionDialog), new Dictionary<string, object> { ["Request"] = request });

    private void Delete(ExplorerNode n) => Run(n, "DeleteObject", DropSql(n),
        database: n.Kind is NodeKind.Login ? "master" : n.Kind is NodeKind.Job ? "msdb" : null,
        refresh: n.Parent, danger: true, confirmKey: "Delete",
        messageKey: n.Kind == NodeKind.Login ? "DeleteLoginHint" : "ConfirmDeleteObject");

    private void Truncate(ExplorerNode n) => ShowAction(new ActionRequest
    {
        TitleKey = "Truncate",
        Subject = n.DisplayName,
        MessageKey = "TruncateHint",
        Server = n.Server,
        Database = n.Database,
        Danger = true,
        ConfirmKey = "Truncate",
        ShowSql = true,
        Refresh = n.Parent,
        Fields = [new ActionField { Key = "delete", LabelKey = "UseDeleteInstead", Type = FieldType.Checkbox, HintKey = "UseDeleteHint" }],
        BuildSql = (r, _) => r.Flag("delete") ? $"DELETE FROM {n.QualifiedName};" : $"TRUNCATE TABLE {n.QualifiedName};"
    });

    private void Rename(ExplorerNode n)
    {
        var old = n.Name!;
        Func<string, string> build = n.Kind switch
        {
            NodeKind.Database => name => $"ALTER DATABASE {Sql.Q(old)} MODIFY NAME = {Sql.Q(name)};",
            NodeKind.Login => name => $"ALTER LOGIN {Sql.Q(old)} WITH NAME = {Sql.Q(name)};",
            NodeKind.User => name => $"ALTER USER {Sql.Q(old)} WITH NAME = {Sql.Q(name)};",
            NodeKind.Role => name => $"ALTER ROLE {Sql.Q(old)} WITH NAME = {Sql.Q(name)};",
            NodeKind.Column => name => $"EXEC sp_rename {Sql.Lit($"{n.ParentQualifiedName}.{Sql.Q(old)}")}, {Sql.Lit(name)}, 'COLUMN';",
            NodeKind.Index => name => $"EXEC sp_rename {Sql.Lit($"{n.ParentQualifiedName}.{Sql.Q(old)}")}, {Sql.Lit(name)}, 'INDEX';",
            NodeKind.Key or NodeKind.Constraint => name => $"EXEC sp_rename {Sql.Lit(Sql.Q(n.ParentSchema!, old))}, {Sql.Lit(name)}, 'OBJECT';",
            _ => name => $"EXEC sp_rename {Sql.Lit(n.QualifiedName)}, {Sql.Lit(name)};"
        };

        var isDatabase = n.Kind == NodeKind.Database;
        var fields = new List<ActionField> { new() { Key = "name", LabelKey = "NewName", Value = old, Required = true } };
        if (isDatabase)
            fields.Add(new ActionField { Key = "close", LabelKey = "CloseConnections", Type = FieldType.Checkbox, Checked = true });

        ShowAction(new ActionRequest
        {
            TitleKey = "Rename",
            Subject = n.Kind == NodeKind.Database ? n.Name : n.DisplayName,
            MessageKey = isDatabase ? "RenameDatabaseHint" : n.Kind is NodeKind.Table or NodeKind.View or NodeKind.Procedure or NodeKind.Function or NodeKind.Column ? "RenameHint" : null,
            Server = n.Server,
            Database = isDatabase || n.Kind == NodeKind.Login ? "master" : n.Database,
            ReleaseDatabase = isDatabase ? old : null,
            Fields = fields,
            Validate = r => r.Text("name") == old ? "NameUnchanged" : null,
            BuildSql = (r, _) =>
            {
                var name = r.Text("name");
                if (isDatabase && r.Flag("close"))
                    return $"ALTER DATABASE {Sql.Q(old)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\nGO\n{build(name)}\nGO\nALTER DATABASE {Sql.Q(name)} SET MULTI_USER;";
                return build(name);
            },
            Refresh = n.Parent
        });
    }

    private void DeleteDatabase(ExplorerNode n)
    {
        var db = n.Name!;
        ShowAction(new ActionRequest
        {
            TitleKey = "DeleteDatabase",
            Subject = db,
            MessageKey = "DeleteDatabaseHint",
            Server = n.Server,
            Database = "master",
            Danger = true,
            ConfirmKey = "Delete",
            ShowSql = true,
            ReleaseDatabase = db,
            Refresh = n.Parent,
            Fields =
            [
                new ActionField { Key = "close", LabelKey = "CloseConnections", Type = FieldType.Checkbox, Checked = n.IsOnline },
                new ActionField { Key = "history", LabelKey = "DeleteBackupHistory", Type = FieldType.Checkbox }
            ],
            BuildSql = (r, _) =>
            {
                var sql = new List<string>();
                if (r.Flag("history"))
                    sql.Add($"EXEC msdb.dbo.sp_delete_database_backuphistory @database_name = {Sql.Lit(db)};");
                if (r.Flag("close"))
                    sql.Add($"ALTER DATABASE {Sql.Q(db)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
                sql.Add($"DROP DATABASE {Sql.Q(db)};");
                return string.Join("\nGO\n", sql);
            },
            OnSuccess = async () =>
            {
                foreach (var tab in session.Tabs.Where(t => t.Server == n.Server && t.Database == db).ToList())
                    await session.CloseTabAsync(tab);
            }
        });
    }

    private void Detach(ExplorerNode n)
    {
        var db = n.Name!;
        ShowAction(new ActionRequest
        {
            TitleKey = "Detach",
            Subject = db,
            MessageKey = "DetachHint",
            Server = n.Server,
            Database = "master",
            ConfirmKey = "Detach",
            ShowSql = true,
            ReleaseDatabase = db,
            Refresh = n.Parent,
            Fields =
            [
                new ActionField { Key = "close", LabelKey = "CloseConnections", Type = FieldType.Checkbox, Checked = n.IsOnline },
                new ActionField { Key = "stats", LabelKey = "UpdateStatisticsBefore", Type = FieldType.Checkbox }
            ],
            BuildSql = (r, _) =>
            {
                var sql = new List<string>();
                if (r.Flag("close"))
                    sql.Add($"ALTER DATABASE {Sql.Q(db)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
                sql.Add($"EXEC master.dbo.sp_detach_db @dbname = {Sql.Lit(db)}, @skipchecks = '{(r.Flag("stats") ? "false" : "true")}';");
                return string.Join("\nGO\n", sql);
            }
        });
    }

    private void Shrink(ExplorerNode n)
    {
        var db = n.Name!;
        ShowAction(new ActionRequest
        {
            TitleKey = "Shrink",
            Subject = db,
            MessageKey = "ShrinkHint",
            Server = n.Server,
            Database = "master",
            KeepOpen = true,
            ShowSql = true,
            Fields = [new ActionField { Key = "free", LabelKey = "ShrinkFreePercent", Type = FieldType.Number, HintKey = "ShrinkFreeHint" }],
            Validate = r => r.Text("free") is { Length: > 0 } v && (!int.TryParse(v, out var p) || p is < 0 or > 99) ? "InvalidPercent" : null,
            BuildSql = (r, _) => r.Text("free") is { Length: > 0 } free
                ? $"DBCC SHRINKDATABASE ({Sql.Lit(db)}, {int.Parse(free)});"
                : $"DBCC SHRINKDATABASE ({Sql.Lit(db)});"
        });
    }

    private void NewCheck(ExplorerNode folder) => ShowAction(new ActionRequest
    {
        TitleKey = "NewCheckConstraint",
        Subject = folder.ParentQualifiedName,
        Server = folder.Server,
        Database = folder.Database,
        Refresh = folder,
        Fields =
        [
            new ActionField { Key = "name", LabelKey = "Name", Value = $"CK_{folder.ParentName}", Required = true },
            new ActionField { Key = "expr", LabelKey = "CheckExpression", Type = FieldType.TextArea, Required = true, HintKey = "CheckExpressionHint" }
        ],
        BuildSql = (r, _) => ScriptBuilder.AddCheck(folder.ParentQualifiedName, new CheckDefinition(r.Text("name"), r.Text("expr"), false))
    });

    private void NewSynonym(ExplorerNode folder) => ShowAction(new ActionRequest
    {
        TitleKey = "NewSynonym",
        Server = folder.Server,
        Database = folder.Database,
        Refresh = folder,
        Fields =
        [
            new ActionField { Key = "schema", LabelKey = "Schema", Value = "dbo", Required = true },
            new ActionField { Key = "name", LabelKey = "Name", Required = true },
            new ActionField { Key = "target", LabelKey = "SynonymTarget", Required = true, HintKey = "SynonymTargetHint" }
        ],
        BuildSql = (r, _) => $"CREATE SYNONYM {Sql.Q(r.Text("schema"), r.Text("name"))} FOR {r.Text("target")};"
    });

    private void NewSequence(ExplorerNode folder) => ShowAction(new ActionRequest
    {
        TitleKey = "NewSequence",
        Server = folder.Server,
        Database = folder.Database,
        Refresh = folder,
        Fields =
        [
            new ActionField { Key = "schema", LabelKey = "Schema", Value = "dbo", Required = true },
            new ActionField { Key = "name", LabelKey = "Name", Required = true },
            new ActionField
            {
                Key = "type", LabelKey = "DataType", Type = FieldType.Select, Value = "bigint",
                Options = [("bigint", "bigint"), ("int", "int"), ("smallint", "smallint"), ("tinyint", "tinyint"), ("decimal(18,0)", "decimal(18,0)")]
            },
            new ActionField { Key = "start", LabelKey = "StartWith", Type = FieldType.Number, Value = "1", Required = true },
            new ActionField { Key = "step", LabelKey = "IncrementBy", Type = FieldType.Number, Value = "1", Required = true },
            new ActionField { Key = "cycle", LabelKey = "Cycle", Type = FieldType.Checkbox }
        ],
        Validate = r => long.TryParse(r.Text("start"), out _) && long.TryParse(r.Text("step"), out var s) && s != 0 ? null : "InvalidNumber",
        BuildSql = (r, _) => $"CREATE SEQUENCE {Sql.Q(r.Text("schema"), r.Text("name"))}\n    AS {r.Text("type")}\n    START WITH {r.Text("start")}\n    INCREMENT BY {r.Text("step")}\n    {(r.Flag("cycle") ? "CYCLE" : "NO CYCLE")};"
    });

    private void NewSchema(ExplorerNode folder) => ShowAction(new ActionRequest
    {
        TitleKey = "NewSchema",
        Server = folder.Server,
        Database = folder.Database,
        Refresh = folder,
        Fields =
        [
            new ActionField { Key = "name", LabelKey = "Name", Required = true },
            new ActionField { Key = "owner", LabelKey = "SchemaOwner", Value = "dbo", HintKey = "SchemaOwnerHint" }
        ],
        BuildSql = (r, _) => $"CREATE SCHEMA {Sql.Q(r.Text("name"))}{(r.Text("owner") is { Length: > 0 } owner ? $" AUTHORIZATION {Sql.Q(owner)}" : "")};"
    });

    private async Task UseForMigrationAsync(ExplorerNode n, bool source)
    {
        if (copyJob.IsLocked)
        {
            session.Toast(l["LockedWhileCopying"], ToastKind.Error);
            return;
        }

        var connection = source ? migration.Source : migration.Destination;
        connection.ConnectionString = n.Server.ConnectionStringFor(n.Name);
        connection.Server = null;
        connection.Error = null;
        try
        {
            connection.Server = await sqlServer.GetServerInfoAsync(connection.ConnectionString);
        }
        catch (Exception ex)
        {
            connection.Error = ex.Message;
        }

        migration.SourceTables = null;
        migration.DestinationTables = null;
        migration.NotifyChanged();
        navigation.NavigateTo("");
    }
}
