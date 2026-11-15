using System.Data.Common;
using System.Text;
using MigrateSQLServerDatabases.Models.Manager;
using Npgsql;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>PostgreSQL. Server → databases → schemas → objects; each database needs its own connection.</summary>
public sealed class PostgresDialect : EngineDialect
{
    private static readonly string[] SystemSchemas = ["pg_catalog", "information_schema", "pg_toast"];

    public override DatabaseEngine Engine => DatabaseEngine.PostgreSql;
    public override string Product => "PostgreSQL";
    public override int DefaultPort => 5432;
    public override string? DefaultDatabase => "postgres";
    protected override bool DollarQuotes => true;
    protected override string CurrentUserSql => "SELECT current_user";

    public override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public override string BuildConnectionString(string host, int port, string user, string password, string? database) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = user,
            Password = password,
            Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database.Trim(),
            Timeout = 15,
            IncludeErrorDetail = true
        }.ConnectionString;

    public override string ServerName(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return builder.Port is 5432 or 0 ? builder.Host ?? "" : $"{builder.Host}:{builder.Port}";
    }

    protected override string Limit(int top) => $"\n LIMIT {top}";

    /// <summary>"schema"."table" as text, for to_regclass().</summary>
    private string RegClass(string? schema, string name) => Name(schema, name);

    // ---------- Object Explorer ----------

    public override List<ExplorerNode> ServerFolders(ExplorerNode server) => Folders(server, NodeKind.DatabasesFolder, NodeKind.RolesFolder);

    public override async Task<List<ExplorerNode>> LoadChildrenAsync(ExplorerNode node)
    {
        var s = node.Server;
        var db = node.Database;
        switch (node.Kind)
        {
            case NodeKind.Server:
                return ServerFolders(node);

            case NodeKind.DatabasesFolder:
                return (await GetDatabaseNamesAsync(s)).Select(n => Item(node, NodeKind.Database, null, n, database: n, canExpand: true)).ToList();

            case NodeKind.Database:
                return Folders(node, NodeKind.SchemasFolder);

            case NodeKind.SchemasFolder or NodeKind.SystemSchemasFolder:
            {
                var system = node.Kind == NodeKind.SystemSchemasFolder;
                var names = await QueryAsync(s, db, "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg\\_temp%' AND nspname NOT LIKE 'pg\\_toast\\_temp%' ORDER BY nspname", r => Str(r, 0));
                var list = system ? new List<ExplorerNode>() : [Folder(node, NodeKind.SystemSchemasFolder)];
                list.AddRange(names.Where(n => SystemSchemas.Contains(n) == system)
                    .Select(n => Item(node, NodeKind.Schema, n, n, canExpand: true, system: system)));
                return list;
            }

            case NodeKind.Schema:
                return Folders(node, NodeKind.TablesFolder, NodeKind.ViewsFolder, NodeKind.FunctionsFolder, NodeKind.SequencesFolder);

            case NodeKind.TablesFolder:
                return (await QueryAsync(s, db, """
                        SELECT c.relname, c.reltuples::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = @s AND c.relkind IN ('r', 'p') ORDER BY c.relname
                        """, r => (Name: Str(r, 0), Rows: NLong(r, 1)), ("s", node.Schema)))
                    .Select(t => Item(node, NodeKind.Table, node.Schema, t.Name, rows: t.Rows is >= 0 ? t.Rows : null, canExpand: true)).ToList();

            case NodeKind.Table:
                return Folders(node, NodeKind.ColumnsFolder, NodeKind.KeysFolder, NodeKind.IndexesFolder, NodeKind.TriggersFolder);

            case NodeKind.View:
                return Folders(node, NodeKind.ColumnsFolder);

            case NodeKind.ViewsFolder:
                return (await QueryAsync(s, db, """
                        SELECT c.relname, c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = @s AND c.relkind IN ('v', 'm') ORDER BY c.relname
                        """, r => (Name: Str(r, 0), Kind: Str(r, 1)), ("s", node.Schema)))
                    .Select(v => Item(node, NodeKind.View, node.Schema, v.Name, detail: v.Kind == "m" ? "materialized" : null, subType: v.Kind, canExpand: true)).ToList();

            case NodeKind.FunctionsFolder:
                return (await QueryAsync(s, db, """
                        SELECT p.proname, pg_get_function_identity_arguments(p.oid), p.prokind
                        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = @s ORDER BY p.proname
                        """, r => (Name: Str(r, 0), Args: Str(r, 1), Kind: Str(r, 2)), ("s", node.Schema)))
                    .Select(f => Item(node, f.Kind == "p" ? NodeKind.Procedure : NodeKind.Function, node.Schema, f.Name,
                        label: $"{f.Name}({f.Args})", detail: f.Kind switch { "p" => "procedure", "a" => "aggregate", "w" => "window", _ => null },
                        subType: f.Args)).ToList();

            case NodeKind.SequencesFolder:
                return (await QueryAsync(s, db, """
                        SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = @s AND c.relkind = 'S' ORDER BY c.relname
                        """, r => Str(r, 0), ("s", node.Schema)))
                    .Select(q => Item(node, NodeKind.Sequence, node.Schema, q)).ToList();

            case NodeKind.ColumnsFolder:
                return await ColumnNodesAsync(node);

            case NodeKind.KeysFolder:
                return (await QueryAsync(s, db, """
                        SELECT conname, contype FROM pg_constraint WHERE conrelid = to_regclass(@t)
                        ORDER BY CASE contype WHEN 'p' THEN 0 WHEN 'u' THEN 1 WHEN 'f' THEN 2 ELSE 3 END, conname
                        """, r => (Name: Str(r, 0), Type: Str(r, 1)), ("t", RegClass(node.ParentSchema, node.ParentName!))))
                    .Select(k => Item(node, NodeKind.Key, null, k.Name,
                        detail: k.Type switch { "p" => "primary key", "u" => "unique", "f" => "foreign key", "c" => "check", _ => k.Type },
                        subType: k.Type switch { "p" => "PK", "u" => "UQ", "f" => "F", _ => "C" })).ToList();

            case NodeKind.IndexesFolder:
                return (await QueryAsync(s, db, """
                        SELECT i.relname, x.indisunique, x.indisprimary FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid
                        WHERE x.indrelid = to_regclass(@t) ORDER BY i.relname
                        """, r => (Name: Str(r, 0), Unique: Bool(r, 1), Primary: Bool(r, 2)), ("t", RegClass(node.ParentSchema, node.ParentName!))))
                    .Select(i => Item(node, NodeKind.Index, node.ParentSchema, i.Name, detail: i.Unique ? "Unique" : "Non-Unique",
                        subType: i.Primary ? "PK" : i.Unique ? "UQ" : "IX")).ToList();

            case NodeKind.TriggersFolder:
                return (await QueryAsync(s, db,
                        "SELECT tgname, tgenabled FROM pg_trigger WHERE tgrelid = to_regclass(@t) AND NOT tgisinternal ORDER BY tgname",
                        r => (Name: Str(r, 0), Disabled: Str(r, 1) == "D"), ("t", RegClass(node.ParentSchema, node.ParentName!))))
                    .Select(t => Item(node, NodeKind.Trigger, node.ParentSchema, t.Name, disabled: t.Disabled)).ToList();

            case NodeKind.RolesFolder:
                return (await QueryAsync(s, null,
                        "SELECT rolname, rolcanlogin, rolsuper FROM pg_roles WHERE rolname NOT LIKE 'pg\\_%' ORDER BY rolname",
                        r => (Name: Str(r, 0), Login: Bool(r, 1), Super: Bool(r, 2))))
                    .Select(r => Item(node, NodeKind.Role, null, r.Name, detail: r.Super ? "superuser" : r.Login ? "login" : null)).ToList();

            default:
                return [];
        }
    }

    public override Task<List<string>> GetDatabaseNamesAsync(ManagedServer server) =>
        QueryAsync(server, null, "SELECT datname FROM pg_database WHERE NOT datistemplate AND datallowconn ORDER BY datname", r => Str(r, 0));

    public override Task<List<ColumnKeysInfo>> LoadColumnsAsync(ManagedServer server, string? database, string? schema, string table) =>
        QueryAsync(server, database, """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull,
                   EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid = a.attrelid AND k.contype = 'p' AND a.attnum = ANY (k.conkey)),
                   EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid = a.attrelid AND k.contype = 'u' AND a.attnum = ANY (k.conkey)),
                   fk.nspname, fk.relname, fk.attname,
                   a.attidentity <> '' OR COALESCE(pg_get_expr(d.adbin, d.adrelid), '') LIKE 'nextval(%',
                   a.attgenerated <> ''
            FROM pg_attribute a
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            LEFT JOIN LATERAL (
                SELECT rn.nspname, rc.relname, ra.attname
                FROM pg_constraint k
                JOIN pg_class rc ON rc.oid = k.confrelid
                JOIN pg_namespace rn ON rn.oid = rc.relnamespace
                JOIN pg_attribute ra ON ra.attrelid = k.confrelid AND ra.attnum = k.confkey[array_position(k.conkey, a.attnum)]
                WHERE k.conrelid = a.attrelid AND k.contype = 'f' AND a.attnum = ANY (k.conkey)
                LIMIT 1) fk ON true
            WHERE a.attrelid = to_regclass(@t) AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """,
            r => new ColumnKeysInfo(Str(r, 0), Str(r, 1), Bool(r, 2), Bool(r, 3), Bool(r, 4),
                r.IsDBNull(6) ? null : (Str(r, 5), Str(r, 6), Str(r, 7)), Bool(r, 8), Bool(r, 9)),
            ("t", RegClass(schema, table)));

    // ---------- Query tab ----------

    /// <summary>A PostgreSQL session can't switch database; the tab reconnects.</summary>
    public override Task<bool> ChangeDatabaseAsync(DbConnection connection, string database) => Task.FromResult(false);

    public override IDisposable? HookMessages(DbConnection connection, Action<string> message)
    {
        if (connection is not NpgsqlConnection npgsql)
            return null;
        NoticeEventHandler handler = (_, e) => message($"{e.Notice.Severity}: {e.Notice.MessageText}");
        npgsql.Notice += handler;
        return new Unhook(() => npgsql.Notice -= handler);
    }

    // ---------- Scripting ----------

    public override async Task<string> ScriptCreateAsync(ExplorerNode node)
    {
        var s = node.Server;
        var db = node.Database;
        switch (node.Kind)
        {
            case NodeKind.Table:
                return await ScriptTableAsync(s, db, node.Schema, node.Name!);
            case NodeKind.Column:
                return await ScriptTableAsync(s, db, node.ParentSchema, node.ParentName!);
            case NodeKind.View:
            {
                var definition = await ScalarAsync(s, db, "SELECT pg_get_viewdef(to_regclass(@t), true)", ("t", node.QualifiedName));
                var kind = node.SubType == "m" ? "MATERIALIZED VIEW" : "OR REPLACE VIEW";
                return $"CREATE {kind} {node.QualifiedName} AS\n{definition}\n";
            }
            case NodeKind.Function or NodeKind.Procedure:
            {
                var definition = await ScalarAsync(s, db, "SELECT pg_get_functiondef(to_regprocedure(@f))", ("f", $"{node.QualifiedName}({node.SubType})"));
                return $"{definition};\n";
            }
            case NodeKind.Trigger:
                return $"{await ScalarAsync(s, db, "SELECT pg_get_triggerdef(oid, true) FROM pg_trigger WHERE tgrelid = to_regclass(@t) AND tgname = @n",
                    ("t", node.ParentQualifiedName), ("n", node.Name))};\n";
            case NodeKind.Index:
                return $"{await ScalarAsync(s, db, "SELECT pg_get_indexdef(to_regclass(@i))", ("i", Name(node.Schema, node.Name)))};\n";
            case NodeKind.Key:
                return $"ALTER TABLE {node.ParentQualifiedName} ADD CONSTRAINT {Q(node.Name)} " +
                       $"{await ScalarAsync(s, db, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = to_regclass(@t) AND conname = @n", ("t", node.ParentQualifiedName), ("n", node.Name))};\n";
            case NodeKind.Sequence:
            {
                var rows = await QueryAsync(s, db, """
                    SELECT start_value, increment_by, min_value, max_value, cycle FROM pg_sequences WHERE schemaname = @s AND sequencename = @n
                    """, r => $"CREATE SEQUENCE {node.QualifiedName}\n    START WITH {Str(r, 0)}\n    INCREMENT BY {Str(r, 1)}\n    MINVALUE {Str(r, 2)}\n    MAXVALUE {Str(r, 3)}\n    {(Bool(r, 4) ? "CYCLE" : "NO CYCLE")};\n",
                    ("s", node.Schema), ("n", node.Name));
                return rows.FirstOrDefault() ?? throw new InvalidOperationException($"{node.QualifiedName} was not found.");
            }
            case NodeKind.Schema:
                return $"CREATE SCHEMA {Q(node.Name)};\n";
            case NodeKind.Database:
                return $"CREATE DATABASE {Q(node.Name)};\n";
            default:
                throw new NotSupportedException();
        }
    }

    /// <summary>CREATE TABLE built from the catalog: columns, constraints, then the other indexes.</summary>
    private async Task<string> ScriptTableAsync(ManagedServer server, string? database, string? schema, string table)
    {
        var name = Name(schema, table);
        var columns = await QueryAsync(server, database, """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull, pg_get_expr(d.adbin, d.adrelid), NULLIF(a.attidentity::text, ''), NULLIF(a.attgenerated::text, '')
            FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE a.attrelid = to_regclass(@t) AND a.attnum > 0 AND NOT a.attisdropped ORDER BY a.attnum
            """,
            r =>
            {
                var line = new StringBuilder($"    {Q(Str(r, 0))} {Str(r, 1)}");
                var identity = Str(r, 4);
                var generated = Str(r, 5);
                if (identity.Length > 0)
                    line.Append(identity == "a" ? " GENERATED ALWAYS AS IDENTITY" : " GENERATED BY DEFAULT AS IDENTITY");
                else if (generated.Length > 0)
                    line.Append($" GENERATED ALWAYS AS ({Str(r, 3)}) STORED");
                else if (!r.IsDBNull(3))
                    line.Append($" DEFAULT {Str(r, 3)}");
                if (Bool(r, 2))
                    line.Append(" NOT NULL");
                return line.ToString();
            },
            ("t", name));

        var constraints = await QueryAsync(server, database, """
            SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = to_regclass(@t)
            ORDER BY CASE contype WHEN 'p' THEN 0 WHEN 'u' THEN 1 WHEN 'c' THEN 2 ELSE 3 END, conname
            """, r => $"    CONSTRAINT {Q(Str(r, 0))} {Str(r, 1)}", ("t", name));

        var indexes = await QueryAsync(server, database, """
            SELECT pg_get_indexdef(x.indexrelid) FROM pg_index x
            WHERE x.indrelid = to_regclass(@t) AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = x.indexrelid)
            """, r => Str(r, 0) + ";", ("t", name));

        var sb = new StringBuilder($"CREATE TABLE {name} (\n");
        sb.Append(string.Join(",\n", columns.Concat(constraints)));
        sb.Append("\n);\n");
        foreach (var index in indexes)
            sb.Append(index).Append('\n');
        return sb.ToString();
    }

    public override string? DropSql(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Table => $"DROP TABLE {node.QualifiedName};",
        NodeKind.View => $"DROP {(node.SubType == "m" ? "MATERIALIZED VIEW" : "VIEW")} {node.QualifiedName};",
        NodeKind.Function => $"DROP FUNCTION {node.QualifiedName}({node.SubType});",
        NodeKind.Procedure => $"DROP PROCEDURE {node.QualifiedName}({node.SubType});",
        NodeKind.Sequence => $"DROP SEQUENCE {node.QualifiedName};",
        NodeKind.Index when node.SubType is "PK" or "UQ" => $"ALTER TABLE {node.ParentQualifiedName} DROP CONSTRAINT {Q(node.Name)};",
        NodeKind.Index => $"DROP INDEX {Name(node.Schema, node.Name)};",
        NodeKind.Key => $"ALTER TABLE {node.ParentQualifiedName} DROP CONSTRAINT {Q(node.Name)};",
        NodeKind.Trigger => $"DROP TRIGGER {Q(node.Name)} ON {node.ParentQualifiedName};",
        NodeKind.Column => $"ALTER TABLE {node.ParentQualifiedName} DROP COLUMN {Q(node.Name)};",
        NodeKind.Schema => $"DROP SCHEMA {Q(node.Name)};",
        NodeKind.Database => $"DROP DATABASE {Q(node.Name)};",
        NodeKind.Role => $"DROP ROLE {Q(node.Name)};",
        _ => null
    };

    public override string? RenameSql(ExplorerNode node, string newName) => node.Kind switch
    {
        NodeKind.Table => $"ALTER TABLE {node.QualifiedName} RENAME TO {Q(newName)};",
        NodeKind.View => $"ALTER {(node.SubType == "m" ? "MATERIALIZED VIEW" : "VIEW")} {node.QualifiedName} RENAME TO {Q(newName)};",
        NodeKind.Column => $"ALTER TABLE {node.ParentQualifiedName} RENAME COLUMN {Q(node.Name)} TO {Q(newName)};",
        NodeKind.Index => $"ALTER INDEX {Name(node.Schema, node.Name)} RENAME TO {Q(newName)};",
        NodeKind.Key => $"ALTER TABLE {node.ParentQualifiedName} RENAME CONSTRAINT {Q(node.Name)} TO {Q(newName)};",
        NodeKind.Sequence => $"ALTER SEQUENCE {node.QualifiedName} RENAME TO {Q(newName)};",
        NodeKind.Function => $"ALTER FUNCTION {node.QualifiedName}({node.SubType}) RENAME TO {Q(newName)};",
        NodeKind.Procedure => $"ALTER PROCEDURE {node.QualifiedName}({node.SubType}) RENAME TO {Q(newName)};",
        NodeKind.Schema => $"ALTER SCHEMA {Q(node.Name)} RENAME TO {Q(newName)};",
        NodeKind.Database => $"ALTER DATABASE {Q(node.Name)} RENAME TO {Q(newName)};",
        NodeKind.Role => $"ALTER ROLE {Q(node.Name)} RENAME TO {Q(newName)};",
        _ => null
    };

    public override string? CreateDatabaseSql(string name) => $"CREATE DATABASE {Q(name)} ENCODING 'UTF8';";

    public override string? CreateSchemaSql(string name) => $"CREATE SCHEMA {Q(name)};";

    public override string NewTableTemplate(string? schema) => $"""
        CREATE TABLE {Name(schema, "new_table")} (
            id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name varchar(100) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    public override string NewViewTemplate(string? schema) => $"""
        CREATE VIEW {Name(schema, "new_view")} AS
        SELECT *
          FROM {Name(schema, "table_name")};
        """;

    public override string NewProcedureTemplate(string? schema) => $"""
        CREATE PROCEDURE {Name(schema, "new_procedure")}(p_id integer)
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE NOTICE 'id = %', p_id;
        END;
        $$;
        """;

    public override string NewFunctionTemplate(string? schema) => $"""
        CREATE FUNCTION {Name(schema, "new_function")}(p_value integer)
        RETURNS integer
        LANGUAGE sql
        AS $$
            SELECT p_value * 2;
        $$;
        """;

    public override string ActivitySql => """
        -- Sessions on the server. To end one: SELECT pg_terminate_backend(<pid>);
        SELECT pid, usename AS "user", client_addr, datname AS database, application_name, state,
               wait_event_type, wait_event, now() - query_start AS running_for, query
          FROM pg_stat_activity
         WHERE backend_type = 'client backend'
         ORDER BY query_start NULLS LAST;
        """;

    public override string SettingsSql => """
        SELECT version();
        SELECT name, setting, unit, category, short_desc FROM pg_settings ORDER BY name;
        """;
}
