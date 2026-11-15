using System.Data.Common;
using MigrateSQLServerDatabases.Models.Manager;
using MySqlConnector;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>MySQL / MariaDB. A schema is a database, so tables are named `database`.`table`.</summary>
public sealed class MySqlDialect : EngineDialect
{
    private static readonly string[] SystemDatabases = ["information_schema", "mysql", "performance_schema", "sys"];

    public override DatabaseEngine Engine => DatabaseEngine.MySql;
    public override string Product => "MySQL";
    public override int DefaultPort => 3306;
    protected override char QuoteOpen => '`';
    protected override char QuoteClose => '`';
    protected override string Quotes => "'\"`";
    protected override bool HashComments => true;
    protected override bool BackslashEscapes => true;
    protected override bool DelimiterCommand => true;
    protected override string CurrentUserSql => "SELECT CURRENT_USER()";

    public override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    public override string BuildConnectionString(string host, int port, string user, string password, string? database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            AllowUserVariables = true,
            // MySQL 8's default sign-in needs the server key when the connection isn't encrypted
            AllowPublicKeyRetrieval = true,
            ConvertZeroDateTime = true,
            ConnectionTimeout = 15
        };
        if (!string.IsNullOrWhiteSpace(database))
            builder.Database = database.Trim();
        return builder.ConnectionString;
    }

    public override string ServerName(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        return builder.Port is 3306 or 0 ? builder.Server : $"{builder.Server}:{builder.Port}";
    }

    // MySQL strings treat backslash as an escape character
    public override string Literal(string? value) => value is null ? "NULL" : "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";

    protected override string Limit(int top) => $"\n LIMIT {top}";

    // ---------- Object Explorer ----------

    public override List<ExplorerNode> ServerFolders(ExplorerNode server) => Folders(server, NodeKind.DatabasesFolder, NodeKind.LoginsFolder);

    public override async Task<List<ExplorerNode>> LoadChildrenAsync(ExplorerNode node)
    {
        var s = node.Server;
        var db = node.Database;
        switch (node.Kind)
        {
            case NodeKind.Server:
                return ServerFolders(node);

            case NodeKind.DatabasesFolder:
            {
                var names = await GetAllDatabasesAsync(s);
                var list = new List<ExplorerNode> { Folder(node, NodeKind.SystemDatabasesFolder) };
                list.AddRange(names.Where(n => !SystemDatabases.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Select(n => Item(node, NodeKind.Database, null, n, database: n, canExpand: true)));
                return list;
            }

            case NodeKind.SystemDatabasesFolder:
                return (await GetAllDatabasesAsync(s)).Where(n => SystemDatabases.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Select(n => Item(node, NodeKind.Database, null, n, database: n, canExpand: true, system: true)).ToList();

            case NodeKind.Database:
                return Folders(node, NodeKind.TablesFolder, NodeKind.ViewsFolder, NodeKind.ProceduresFolder, NodeKind.FunctionsFolder);

            case NodeKind.TablesFolder:
                return (await QueryAsync(s, null, """
                        SELECT table_name, table_rows FROM information_schema.tables
                        WHERE table_schema = @db AND table_type = 'BASE TABLE' ORDER BY table_name
                        """, r => (Name: Str(r, 0), Rows: NLong(r, 1)), ("db", db)))
                    .Select(t => Item(node, NodeKind.Table, db, t.Name, rows: t.Rows, canExpand: true)).ToList();

            case NodeKind.Table:
                return Folders(node, NodeKind.ColumnsFolder, NodeKind.KeysFolder, NodeKind.IndexesFolder, NodeKind.TriggersFolder);

            case NodeKind.View:
                return Folders(node, NodeKind.ColumnsFolder);

            case NodeKind.ViewsFolder:
                return (await QueryAsync(s, null,
                        "SELECT table_name FROM information_schema.views WHERE table_schema = @db ORDER BY table_name",
                        r => Str(r, 0), ("db", db)))
                    .Select(v => Item(node, NodeKind.View, db, v, canExpand: true)).ToList();

            case NodeKind.ProceduresFolder or NodeKind.FunctionsFolder:
            {
                var kind = node.Kind == NodeKind.ProceduresFolder ? "PROCEDURE" : "FUNCTION";
                return (await QueryAsync(s, null,
                        "SELECT routine_name FROM information_schema.routines WHERE routine_schema = @db AND routine_type = @type ORDER BY routine_name",
                        r => Str(r, 0), ("db", db), ("type", kind)))
                    .Select(n => Item(node, kind == "PROCEDURE" ? NodeKind.Procedure : NodeKind.Function, db, n)).ToList();
            }

            case NodeKind.ColumnsFolder:
                return await ColumnNodesAsync(node);

            case NodeKind.KeysFolder:
                return (await QueryAsync(s, null, """
                        SELECT constraint_name, constraint_type FROM information_schema.table_constraints
                        WHERE table_schema = @db AND table_name = @t
                        ORDER BY FIELD(constraint_type, 'PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY', 'CHECK'), constraint_name
                        """, r => (Name: Str(r, 0), Type: Str(r, 1)), ("db", db), ("t", node.ParentName)))
                    .Select(k => Item(node, NodeKind.Key, null, k.Name, detail: k.Type.ToLowerInvariant(),
                        subType: k.Type switch { "PRIMARY KEY" => "PK", "UNIQUE" => "UQ", "FOREIGN KEY" => "F", _ => "C" })).ToList();

            case NodeKind.IndexesFolder:
                return (await QueryAsync(s, null, """
                        SELECT index_name, MIN(non_unique), GROUP_CONCAT(column_name ORDER BY seq_in_index SEPARATOR ', ')
                        FROM information_schema.statistics WHERE table_schema = @db AND table_name = @t
                        GROUP BY index_name ORDER BY index_name
                        """, r => (Name: Str(r, 0), Unique: !Bool(r, 1), Columns: Str(r, 2)), ("db", db), ("t", node.ParentName)))
                    .Select(i => Item(node, NodeKind.Index, null, i.Name, detail: $"{(i.Unique ? "Unique" : "Non-Unique")}, {i.Columns}",
                        subType: i.Name == "PRIMARY" ? "PK" : i.Unique ? "UQ" : "IX")).ToList();

            case NodeKind.TriggersFolder:
                return (await QueryAsync(s, null, """
                        SELECT trigger_name, action_timing, event_manipulation FROM information_schema.triggers
                        WHERE event_object_schema = @db AND event_object_table = @t ORDER BY trigger_name
                        """, r => (Name: Str(r, 0), Detail: $"{Str(r, 1)} {Str(r, 2)}".ToLowerInvariant()), ("db", db), ("t", node.ParentName)))
                    .Select(t => Item(node, NodeKind.Trigger, db, t.Name, detail: t.Detail)).ToList();

            case NodeKind.LoginsFolder:
                return (await QueryAsync(s, null, "SELECT user, host FROM mysql.user ORDER BY user, host",
                        r => (User: Str(r, 0), Host: Str(r, 1))))
                    .Select(u => Item(node, NodeKind.Login, null, u.User, label: $"{u.User}@{u.Host}", subType: u.Host,
                        system: u.User.StartsWith("mysql.", StringComparison.Ordinal))).ToList();

            default:
                return [];
        }
    }

    private Task<List<string>> GetAllDatabasesAsync(ManagedServer server) =>
        QueryAsync(server, null, "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name", r => Str(r, 0));

    public override Task<List<string>> GetDatabaseNamesAsync(ManagedServer server) => GetAllDatabasesAsync(server);

    public override Task<List<ColumnKeysInfo>> LoadColumnsAsync(ManagedServer server, string? database, string? schema, string table) =>
        QueryAsync(server, null, """
            SELECT c.column_name, c.column_type, c.is_nullable = 'YES', c.column_key = 'PRI', c.column_key = 'UNI',
                   k.referenced_table_schema, k.referenced_table_name, k.referenced_column_name,
                   c.extra LIKE '%auto_increment%', (c.extra LIKE '%VIRTUAL GENERATED%' OR c.extra LIKE '%STORED GENERATED%')
            FROM information_schema.columns c
            LEFT JOIN information_schema.key_column_usage k
                   ON k.table_schema = c.table_schema AND k.table_name = c.table_name AND k.column_name = c.column_name
                  AND k.referenced_table_name IS NOT NULL
            WHERE c.table_schema = @db AND c.table_name = @t
            ORDER BY c.ordinal_position
            """,
            r => new ColumnKeysInfo(Str(r, 0), Str(r, 1), Bool(r, 2), Bool(r, 3), Bool(r, 4),
                r.IsDBNull(6) ? null : (Str(r, 5), Str(r, 6), Str(r, 7)), Bool(r, 8), Bool(r, 9)),
            ("db", schema ?? database), ("t", table))
        .ContinueWith(t => t.Result.GroupBy(c => c.Name).Select(g => g.First()).ToList());

    // ---------- Query tab ----------

    public override async Task<bool> ChangeDatabaseAsync(DbConnection connection, string database)
    {
        await connection.ChangeDatabaseAsync(database);
        return true;
    }

    public override async Task<string?> CurrentDatabaseAsync(DbConnection connection)
    {
        await using var command = Command(connection, "SELECT DATABASE()");
        return await command.ExecuteScalarAsync() as string;
    }

    public override IDisposable? HookMessages(DbConnection connection, Action<string> message)
    {
        if (connection is not MySqlConnection mysql)
            return null;
        MySqlInfoMessageEventHandler handler = (_, e) =>
        {
            foreach (var error in e.Errors)
                message($"{error.Level} {(int)error.ErrorCode}: {error.Message}");
        };
        mysql.InfoMessage += handler;
        return new Unhook(() => mysql.InfoMessage -= handler);
    }

    // ---------- Scripting ----------

    public override async Task<string> ScriptCreateAsync(ExplorerNode node)
    {
        var s = node.Server;
        switch (node.Kind)
        {
            case NodeKind.Table or NodeKind.Column or NodeKind.Key or NodeKind.Index:
            {
                var table = node.Kind == NodeKind.Table ? node.QualifiedName : Name(node.Database, node.ParentName);
                return await ShowCreateAsync(s, $"SHOW CREATE TABLE {table}", 1) + ";\n";
            }
            case NodeKind.View:
                return await ShowCreateAsync(s, $"SHOW CREATE VIEW {node.QualifiedName}", 1) + ";\n";
            case NodeKind.Procedure:
                return Routine(await ShowCreateAsync(s, $"SHOW CREATE PROCEDURE {node.QualifiedName}", 2));
            case NodeKind.Function:
                return Routine(await ShowCreateAsync(s, $"SHOW CREATE FUNCTION {node.QualifiedName}", 2));
            case NodeKind.Trigger:
                return Routine(await ShowCreateAsync(s, $"SHOW CREATE TRIGGER {node.QualifiedName}", 2));
            case NodeKind.Database:
                return await ShowCreateAsync(s, $"SHOW CREATE DATABASE {Q(node.Name)}", 1) + ";\n";
            default:
                throw new NotSupportedException();
        }
    }

    private static string Routine(string body) => $"DELIMITER $$\n{body}$$\nDELIMITER ;\n";

    private async Task<string> ShowCreateAsync(ManagedServer server, string sql, int column)
    {
        var rows = await QueryAsync(server, null, sql, r => NStr(r, column));
        return rows.FirstOrDefault() ?? throw new InvalidOperationException("The definition is not available (missing privileges?).");
    }

    private string TableOf(ExplorerNode node) => Name(node.Database, node.ParentName);

    public override string? DropSql(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Table => $"DROP TABLE {node.QualifiedName};",
        NodeKind.View => $"DROP VIEW {node.QualifiedName};",
        NodeKind.Procedure => $"DROP PROCEDURE {node.QualifiedName};",
        NodeKind.Function => $"DROP FUNCTION {node.QualifiedName};",
        NodeKind.Trigger => $"DROP TRIGGER {node.QualifiedName};",
        NodeKind.Column => $"ALTER TABLE {TableOf(node)} DROP COLUMN {Q(node.Name)};",
        NodeKind.Index when node.SubType == "PK" => $"ALTER TABLE {TableOf(node)} DROP PRIMARY KEY;",
        NodeKind.Index => $"DROP INDEX {Q(node.Name)} ON {TableOf(node)};",
        NodeKind.Key => node.SubType switch
        {
            "PK" => $"ALTER TABLE {TableOf(node)} DROP PRIMARY KEY;",
            "F" => $"ALTER TABLE {TableOf(node)} DROP FOREIGN KEY {Q(node.Name)};",
            "UQ" => $"ALTER TABLE {TableOf(node)} DROP INDEX {Q(node.Name)};",
            _ => $"ALTER TABLE {TableOf(node)} DROP CHECK {Q(node.Name)};"
        },
        NodeKind.Database => $"DROP DATABASE {Q(node.Name)};",
        NodeKind.Login => $"DROP USER {Literal(node.Name)}@{Literal(node.SubType)};",
        _ => null
    };

    public override string? RenameSql(ExplorerNode node, string newName) => node.Kind switch
    {
        NodeKind.Table or NodeKind.View => $"RENAME TABLE {node.QualifiedName} TO {Name(node.Schema, newName)};",
        NodeKind.Column => $"ALTER TABLE {TableOf(node)} RENAME COLUMN {Q(node.Name)} TO {Q(newName)};",
        NodeKind.Index when node.SubType != "PK" => $"ALTER TABLE {TableOf(node)} RENAME INDEX {Q(node.Name)} TO {Q(newName)};",
        _ => null
    };

    public override string? CreateDatabaseSql(string name) =>
        $"CREATE DATABASE {Q(name)}\n  CHARACTER SET utf8mb4\n  COLLATE utf8mb4_unicode_ci;";

    public override string NewTableTemplate(string? schema) => $"""
        CREATE TABLE {Name(schema, "new_table")} (
            id INT NOT NULL AUTO_INCREMENT,
            name VARCHAR(100) NOT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (id)
        ) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;
        """;

    public override string NewViewTemplate(string? schema) => $"""
        CREATE VIEW {Name(schema, "new_view")} AS
        SELECT *
          FROM {Name(schema, "table_name")};
        """;

    public override string NewProcedureTemplate(string? schema) => $"""
        DELIMITER $$
        CREATE PROCEDURE {Name(schema, "new_procedure")} (IN p_id INT)
        BEGIN
            SELECT p_id;
        END$$
        DELIMITER ;
        """;

    public override string NewFunctionTemplate(string? schema) => $"""
        DELIMITER $$
        CREATE FUNCTION {Name(schema, "new_function")} (p_value INT)
        RETURNS INT
        DETERMINISTIC
        BEGIN
            RETURN p_value * 2;
        END$$
        DELIMITER ;
        """;

    public override string ActivitySql => """
        -- Sessions on the server. To end one: KILL <id>;
        SELECT id, user, host, db, command, time AS seconds, state, info AS query
          FROM information_schema.processlist
         ORDER BY time DESC;
        """;

    public override string SettingsSql => """
        SELECT VERSION() AS version, @@version_comment AS edition, @@hostname AS host, @@port AS port,
               @@character_set_server AS charset, @@collation_server AS collation;
        SHOW VARIABLES;
        """;
}

/// <summary>Runs an action when disposed (removes an event handler).</summary>
public sealed class Unhook(Action action) : IDisposable
{
    public void Dispose() => action();
}
