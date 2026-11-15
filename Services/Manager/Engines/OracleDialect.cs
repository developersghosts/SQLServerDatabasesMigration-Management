using System.Data.Common;
using System.Text.RegularExpressions;
using MigrateSQLServerDatabases.Models.Manager;
using Oracle.ManagedDataAccess.Client;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>Oracle (12c and later). Schemas are users; the query tab switches with ALTER SESSION SET CURRENT_SCHEMA.</summary>
public sealed partial class OracleDialect : EngineDialect
{
    public override DatabaseEngine Engine => DatabaseEngine.Oracle;
    public override string Product => "Oracle";
    public override int DefaultPort => 1521;
    public override string DatabaseLabelKey => "ServiceName";
    public override string? DefaultDatabase => "FREEPDB1";
    protected override string ParameterPrefix => ":";
    protected override bool SlashTerminator => true;
    protected override string CurrentUserSql => "SELECT USER FROM dual";

    [GeneratedRegex(@"^\s*(BEGIN|DECLARE|CREATE\s+(OR\s+REPLACE\s+)?((NON)?EDITIONABLE\s+)?(PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE|LIBRARY))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlSqlStart();

    protected override bool IsBlockStart(string code) => PlSqlStart().IsMatch(code);

    public override DbConnection CreateConnection(string connectionString) => new OracleConnection(connectionString);

    protected override void PrepareCommand(DbCommand command)
    {
        if (command is OracleCommand oracle)
        {
            oracle.BindByName = true;
            // Large values (CLOB, LONG) are read in full
            oracle.InitialLONGFetchSize = -1;
        }
    }

    public override string BuildConnectionString(string host, int port, string user, string password, string? database) =>
        new OracleConnectionStringBuilder
        {
            DataSource = $"{host}:{port}/{(string.IsNullOrWhiteSpace(database) ? DefaultDatabase : database.Trim())}",
            UserID = user,
            Password = password,
            ConnectionTimeout = 15
        }.ConnectionString;

    public override string ServerName(string connectionString) => new OracleConnectionStringBuilder(connectionString).DataSource;

    protected override string Limit(int top) => $"\n FETCH FIRST {top} ROWS ONLY";

    protected override async Task AfterOpenAsync(DbConnection connection, string? database, CancellationToken cancellationToken)
    {
        // The driver takes the language from the Windows locale; keep server messages in English like the other engines
        var sql = "ALTER SESSION SET NLS_LANGUAGE = 'AMERICAN'";
        if (!string.IsNullOrEmpty(database))
            sql += $" CURRENT_SCHEMA = {Q(database)}";
        await using var command = Command(connection, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---------- Object Explorer ----------

    public override List<ExplorerNode> ServerFolders(ExplorerNode server) => Folders(server, NodeKind.SchemasFolder);

    public override async Task<List<ExplorerNode>> LoadChildrenAsync(ExplorerNode node)
    {
        var s = node.Server;
        var owner = node.Schema;
        switch (node.Kind)
        {
            case NodeKind.Server:
                return ServerFolders(node);

            case NodeKind.SchemasFolder or NodeKind.SystemSchemasFolder:
            {
                var system = node.Kind == NodeKind.SystemSchemasFolder;
                var users = await QueryAsync(s, null, "SELECT username, oracle_maintained FROM all_users ORDER BY username",
                    r => (Name: Str(r, 0), System: Str(r, 1) == "Y"));
                var list = system ? new List<ExplorerNode>() : [Folder(node, NodeKind.SystemSchemasFolder)];
                list.AddRange(users.Where(u => u.System == system).Select(u => Item(node, NodeKind.Schema, u.Name, u.Name, canExpand: true, system: system)));
                return list;
            }

            case NodeKind.Schema:
                return Folders(node, NodeKind.TablesFolder, NodeKind.ViewsFolder, NodeKind.ProceduresFolder, NodeKind.FunctionsFolder,
                    NodeKind.PackagesFolder, NodeKind.SequencesFolder, NodeKind.SynonymsFolder);

            case NodeKind.TablesFolder:
                return (await QueryAsync(s, null, """
                        SELECT table_name, num_rows FROM all_tables
                        WHERE owner = :s AND nested = 'NO' AND table_name NOT LIKE 'BIN$%' ORDER BY table_name
                        """, r => (Name: Str(r, 0), Rows: NLong(r, 1)), ("s", owner)))
                    .Select(t => Item(node, NodeKind.Table, owner, t.Name, rows: t.Rows, canExpand: true)).ToList();

            case NodeKind.Table:
                return Folders(node, NodeKind.ColumnsFolder, NodeKind.KeysFolder, NodeKind.IndexesFolder, NodeKind.TriggersFolder);

            case NodeKind.View:
                return Folders(node, NodeKind.ColumnsFolder);

            case NodeKind.ViewsFolder:
                return (await QueryAsync(s, null, "SELECT view_name FROM all_views WHERE owner = :s ORDER BY view_name", r => Str(r, 0), ("s", owner)))
                    .Select(v => Item(node, NodeKind.View, owner, v, canExpand: true)).ToList();

            case NodeKind.ProceduresFolder or NodeKind.FunctionsFolder or NodeKind.PackagesFolder:
            {
                var (type, kind) = node.Kind switch
                {
                    NodeKind.ProceduresFolder => ("PROCEDURE", NodeKind.Procedure),
                    NodeKind.FunctionsFolder => ("FUNCTION", NodeKind.Function),
                    _ => ("PACKAGE", NodeKind.Package)
                };
                return (await QueryAsync(s, null, "SELECT object_name, status FROM all_objects WHERE owner = :s AND object_type = :t ORDER BY object_name",
                        r => (Name: Str(r, 0), Invalid: Str(r, 1) != "VALID"), ("s", owner), ("t", type)))
                    .Select(o => Item(node, kind, owner, o.Name, detail: o.Invalid ? "invalid" : null, disabled: o.Invalid)).ToList();
            }

            case NodeKind.SequencesFolder:
                return (await QueryAsync(s, null, "SELECT sequence_name FROM all_sequences WHERE sequence_owner = :s ORDER BY sequence_name",
                        r => Str(r, 0), ("s", owner)))
                    .Select(q => Item(node, NodeKind.Sequence, owner, q)).ToList();

            case NodeKind.SynonymsFolder:
                return (await QueryAsync(s, null, "SELECT synonym_name, table_owner, table_name FROM all_synonyms WHERE owner = :s ORDER BY synonym_name",
                        r => (Name: Str(r, 0), Target: $"{Str(r, 1)}.{Str(r, 2)}"), ("s", owner)))
                    .Select(y => Item(node, NodeKind.Synonym, owner, y.Name, detail: y.Target)).ToList();

            case NodeKind.ColumnsFolder:
                return await ColumnNodesAsync(node);

            case NodeKind.KeysFolder:
                return (await QueryAsync(s, null, """
                        SELECT constraint_name, constraint_type, status FROM all_constraints
                        WHERE owner = :s AND table_name = :t AND (constraint_type IN ('P', 'U', 'R') OR (constraint_type = 'C' AND generated = 'USER NAME'))
                        ORDER BY DECODE(constraint_type, 'P', 0, 'U', 1, 'R', 2, 3), constraint_name
                        """, r => (Name: Str(r, 0), Type: Str(r, 1), Disabled: Str(r, 2) == "DISABLED"), ("s", node.ParentSchema), ("t", node.ParentName)))
                    .Select(k => Item(node, NodeKind.Key, node.ParentSchema, k.Name,
                        detail: k.Type switch { "P" => "primary key", "U" => "unique", "R" => "foreign key", _ => "check" },
                        subType: k.Type switch { "P" => "PK", "U" => "UQ", "R" => "F", _ => "C" }, disabled: k.Disabled)).ToList();

            case NodeKind.IndexesFolder:
                return (await QueryAsync(s, null, """
                        SELECT index_name, owner, uniqueness FROM all_indexes WHERE table_owner = :s AND table_name = :t ORDER BY index_name
                        """, r => (Name: Str(r, 0), Owner: Str(r, 1), Unique: Str(r, 2) == "UNIQUE"), ("s", node.ParentSchema), ("t", node.ParentName)))
                    .Select(i => Item(node, NodeKind.Index, i.Owner, i.Name, detail: i.Unique ? "Unique" : "Non-Unique", subType: i.Unique ? "UQ" : "IX")).ToList();

            case NodeKind.TriggersFolder:
                return (await QueryAsync(s, null, """
                        SELECT trigger_name, owner, status, trigger_type FROM all_triggers WHERE table_owner = :s AND table_name = :t ORDER BY trigger_name
                        """, r => (Name: Str(r, 0), Owner: Str(r, 1), Disabled: Str(r, 2) == "DISABLED", Type: Str(r, 3).ToLowerInvariant()),
                        ("s", node.ParentSchema), ("t", node.ParentName)))
                    .Select(t => Item(node, NodeKind.Trigger, t.Owner, t.Name, detail: t.Type, disabled: t.Disabled)).ToList();

            default:
                return [];
        }
    }

    public override Task<List<string>> GetDatabaseNamesAsync(ManagedServer server) =>
        QueryAsync(server, null, "SELECT username FROM all_users ORDER BY CASE oracle_maintained WHEN 'N' THEN 0 ELSE 1 END, username", r => Str(r, 0));

    public override Task<List<ColumnKeysInfo>> LoadColumnsAsync(ManagedServer server, string? database, string? schema, string table) =>
        QueryAsync(server, null, """
            SELECT c.column_name,
                   CASE WHEN c.data_type IN ('VARCHAR2', 'NVARCHAR2', 'CHAR', 'NCHAR') THEN c.data_type || '(' || c.char_length || ')'
                        WHEN c.data_type = 'RAW' THEN 'RAW(' || c.data_length || ')'
                        WHEN c.data_type = 'NUMBER' AND c.data_precision IS NOT NULL THEN 'NUMBER(' || c.data_precision || ',' || c.data_scale || ')'
                        ELSE c.data_type END,
                   c.nullable,
                   (SELECT COUNT(*) FROM all_cons_columns cc JOIN all_constraints k ON k.owner = cc.owner AND k.constraint_name = cc.constraint_name
                     WHERE k.constraint_type = 'P' AND cc.owner = c.owner AND cc.table_name = c.table_name AND cc.column_name = c.column_name),
                   (SELECT COUNT(*) FROM all_cons_columns cc JOIN all_constraints k ON k.owner = cc.owner AND k.constraint_name = cc.constraint_name
                     WHERE k.constraint_type = 'U' AND cc.owner = c.owner AND cc.table_name = c.table_name AND cc.column_name = c.column_name),
                   (SELECT MIN(r.owner || '|' || r.table_name || '|' || rcc.column_name)
                      FROM all_cons_columns cc
                      JOIN all_constraints k ON k.owner = cc.owner AND k.constraint_name = cc.constraint_name AND k.constraint_type = 'R'
                      JOIN all_constraints r ON r.owner = k.r_owner AND r.constraint_name = k.r_constraint_name
                      JOIN all_cons_columns rcc ON rcc.owner = r.owner AND rcc.constraint_name = r.constraint_name AND rcc.position = cc.position
                     WHERE cc.owner = c.owner AND cc.table_name = c.table_name AND cc.column_name = c.column_name),
                   c.identity_column, c.virtual_column
            FROM all_tab_cols c
            WHERE c.owner = :s AND c.table_name = :t AND c.hidden_column = 'NO'
            ORDER BY c.column_id
            """,
            r =>
            {
                (string, string, string)? fk = null;
                if (!r.IsDBNull(5) && Str(r, 5).Split('|') is [var o, var t, var col])
                    fk = (o, t, col);
                return new ColumnKeysInfo(Str(r, 0), Str(r, 1), Str(r, 2) == "Y", Bool(r, 3), Bool(r, 4), fk, Str(r, 6) == "YES", Str(r, 7) == "YES");
            },
            ("s", schema), ("t", table));

    // ---------- Query tab ----------

    public override async Task<bool> ChangeDatabaseAsync(DbConnection connection, string database)
    {
        await AfterOpenAsync(connection, database, CancellationToken.None);
        return true;
    }

    public override async Task<string?> CurrentDatabaseAsync(DbConnection connection)
    {
        await using var command = Command(connection, "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM dual");
        return await command.ExecuteScalarAsync() as string;
    }

    public override IDisposable? HookMessages(DbConnection connection, Action<string> message)
    {
        if (connection is not OracleConnection oracle)
            return null;
        OracleInfoMessageEventHandler handler = (_, e) => message(e.Message);
        oracle.InfoMessage += handler;
        return new Unhook(() => oracle.InfoMessage -= handler);
    }

    // ---------- Scripting ----------

    public override async Task<string> ScriptCreateAsync(ExplorerNode node)
    {
        var (type, name, owner) = node.Kind switch
        {
            NodeKind.Table => ("TABLE", node.Name, node.Schema),
            NodeKind.Column => ("TABLE", node.ParentName, node.ParentSchema),
            NodeKind.View => ("VIEW", node.Name, node.Schema),
            NodeKind.Procedure => ("PROCEDURE", node.Name, node.Schema),
            NodeKind.Function => ("FUNCTION", node.Name, node.Schema),
            NodeKind.Package => ("PACKAGE", node.Name, node.Schema),
            NodeKind.Sequence => ("SEQUENCE", node.Name, node.Schema),
            NodeKind.Synonym => ("SYNONYM", node.Name, node.Schema),
            NodeKind.Index => ("INDEX", node.Name, node.Schema),
            NodeKind.Trigger => ("TRIGGER", node.Name, node.Schema),
            NodeKind.Key => (node.SubType == "F" ? "REF_CONSTRAINT" : "CONSTRAINT", node.Name, node.Schema),
            NodeKind.Schema => ("USER", node.Name, null),
            _ => throw new NotSupportedException()
        };

        var ddl = Convert.ToString(await ScalarAsync(node.Server, null,
            "SELECT DBMS_METADATA.GET_DDL(:type, :name, :owner) FROM dual", ("type", type), ("name", name), ("owner", owner)))?.Trim()
            ?? throw new InvalidOperationException($"{name} was not found.");

        var plsql = type is "PROCEDURE" or "FUNCTION" or "PACKAGE" or "TRIGGER";
        if (!plsql)
            return ddl + ";\n";

        // A package comes back as spec and body in one text; each needs its own "/"
        ddl = Regex.Replace(ddl, @"(?<=\S)\s*\n\s*(CREATE OR REPLACE\b)", "\n/\n$1");
        return ddl + "\n/\n";
    }

    public override string? DropSql(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Table => $"DROP TABLE {node.QualifiedName}",
        NodeKind.View => $"DROP VIEW {node.QualifiedName}",
        NodeKind.Procedure => $"DROP PROCEDURE {node.QualifiedName}",
        NodeKind.Function => $"DROP FUNCTION {node.QualifiedName}",
        NodeKind.Package => $"DROP PACKAGE {node.QualifiedName}",
        NodeKind.Sequence => $"DROP SEQUENCE {node.QualifiedName}",
        NodeKind.Synonym => $"DROP SYNONYM {node.QualifiedName}",
        NodeKind.Index => $"DROP INDEX {node.QualifiedName}",
        NodeKind.Trigger => $"DROP TRIGGER {node.QualifiedName}",
        NodeKind.Key => $"ALTER TABLE {node.ParentQualifiedName} DROP CONSTRAINT {Q(node.Name)}",
        NodeKind.Column => $"ALTER TABLE {node.ParentQualifiedName} DROP COLUMN {Q(node.Name)}",
        NodeKind.Schema => $"DROP USER {Q(node.Name)} CASCADE",
        _ => null
    };

    public override string? RenameSql(ExplorerNode node, string newName) => node.Kind switch
    {
        NodeKind.Table => $"ALTER TABLE {node.QualifiedName} RENAME TO {Q(newName)}",
        NodeKind.Column => $"ALTER TABLE {node.ParentQualifiedName} RENAME COLUMN {Q(node.Name)} TO {Q(newName)}",
        NodeKind.Index => $"ALTER INDEX {node.QualifiedName} RENAME TO {Q(newName)}",
        NodeKind.Key => $"ALTER TABLE {node.ParentQualifiedName} RENAME CONSTRAINT {Q(node.Name)} TO {Q(newName)}",
        _ => null
    };

    public override string TruncateSql(ExplorerNode node) => $"TRUNCATE TABLE {node.QualifiedName}";

    public override string? CreateDatabaseSql(string name) => null;

    public override string NewTableTemplate(string? schema) => $"""
        CREATE TABLE {Name(schema, "NEW_TABLE")} (
            ID NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            NAME VARCHAR2(100) NOT NULL,
            CREATED_AT TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
        );
        """;

    public override string NewViewTemplate(string? schema) => $"""
        CREATE OR REPLACE VIEW {Name(schema, "NEW_VIEW")} AS
        SELECT *
          FROM {Name(schema, "TABLE_NAME")};
        """;

    public override string NewProcedureTemplate(string? schema) => $"""
        CREATE OR REPLACE PROCEDURE {Name(schema, "NEW_PROCEDURE")} (p_id IN NUMBER) AS
        BEGIN
            DBMS_OUTPUT.PUT_LINE('id = ' || p_id);
        END;
        /
        """;

    public override string NewFunctionTemplate(string? schema) => $"""
        CREATE OR REPLACE FUNCTION {Name(schema, "NEW_FUNCTION")} (p_value IN NUMBER) RETURN NUMBER AS
        BEGIN
            RETURN p_value * 2;
        END;
        /
        """;

    public override string ActivitySql => """
        -- Sessions on the server (needs SELECT on V$SESSION). To end one: ALTER SYSTEM KILL SESSION '<sid>,<serial#>' IMMEDIATE;
        SELECT sid, serial#, username, machine, program, status, sql_id, last_call_et AS seconds
          FROM v$session
         WHERE type = 'USER'
         ORDER BY last_call_et DESC;
        """;

    public override string SettingsSql => """
        SELECT banner_full FROM v$version;
        SELECT name, value, description FROM v$parameter ORDER BY name;
        """;
}
