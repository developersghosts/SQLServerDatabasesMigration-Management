using System.Text;
using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

public sealed record GenerateScriptOptions(
    bool Tables = true,
    bool Views = true,
    bool Procedures = true,
    bool Functions = true,
    bool Triggers = true,
    bool Synonyms = true,
    bool Data = false,
    int MaxDataRows = 1000,
    bool DropFirst = false,
    bool UseDatabase = true,
    IReadOnlySet<string>? OnlyObjects = null);

/// <summary>Reads object definitions and scripts them (Script As, Modify, Generate Scripts).</summary>
public sealed class SchemaService
{
    public async Task<TableDefinition> GetTableAsync(ManagedServer server, string database, string schema, string name)
    {
        await using var connection = await Db.OpenAsync(server.ConnectionStringFor(database));
        return await GetTableAsync(connection, server, schema, name);
    }

    private static async Task<TableDefinition> GetTableAsync(SqlConnection connection, ManagedServer server, string schema, string name)
    {
        var sparse = server.Major >= 10 ? "c.is_sparse" : "CAST(0 AS bit)";
        var filter = server.Major >= 10 ? "CASE WHEN i.has_filter = 1 THEN i.filter_definition END" : "NULL";
        var sql = $"""
            DECLARE @id int = OBJECT_ID(@name);
            SELECT o.object_id, o.create_date, o.modify_date, CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)),
                   CAST(ISNULL((SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)), 0) AS bigint)
            FROM sys.objects o WHERE o.object_id = @id;

            SELECT c.column_id, c.name, tp.name, SCHEMA_NAME(tp.schema_id), TYPE_NAME(c.system_type_id),
                   CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                   c.max_length, c.precision, c.scale, CAST(c.is_nullable AS int), CAST(c.is_identity AS int), CAST(c.is_computed AS int),
                   c.collation_name, CAST(c.is_rowguidcol AS int), CAST({sparse} AS int),
                   CAST(ic.seed_value AS nvarchar(40)), CAST(ic.increment_value AS nvarchar(40)),
                   cc.definition, CAST(ISNULL(cc.is_persisted, 0) AS int), dc.name, dc.definition
            FROM sys.columns c
            JOIN sys.types tp ON tp.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE c.object_id = @id
            ORDER BY c.column_id;

            SELECT i.index_id, i.name, i.type, i.type_desc, CAST(i.is_unique AS int), CAST(i.is_primary_key AS int),
                   CAST(i.is_unique_constraint AS int), CAST(i.is_disabled AS int), i.fill_factor, {filter}
            FROM sys.indexes i
            WHERE i.object_id = @id AND i.type > 0 AND i.name IS NOT NULL
            ORDER BY i.index_id;

            SELECT ic.index_id, c.name, CAST(ic.is_descending_key AS int), CAST(ic.is_included_column AS int), ic.key_ordinal
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = @id
            ORDER BY ic.index_id, ic.key_ordinal, ic.index_column_id;

            SELECT fk.object_id, fk.name, rs.name, rt.name, fk.delete_referential_action_desc, fk.update_referential_action_desc,
                   CAST(fk.is_disabled AS int), CAST(fk.is_not_trusted AS int)
            FROM sys.foreign_keys fk
            JOIN sys.objects rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            WHERE fk.parent_object_id = @id
            ORDER BY fk.name;

            SELECT fkc.constraint_object_id, pc.name, rc.name
            FROM sys.foreign_key_columns fkc
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fkc.parent_object_id = @id
            ORDER BY fkc.constraint_object_id, fkc.constraint_column_id;

            SELECT name, definition, CAST(is_disabled AS int) FROM sys.check_constraints WHERE parent_object_id = @id ORDER BY name;
            """;

        await using var command = Db.Command(connection, sql, ("@name", Sql.Q(schema, name)));
        await using var r = await command.ExecuteReaderAsync();

        if (!await r.ReadAsync())
            throw new InvalidOperationException($"{Sql.Q(schema, name)} was not found.");

        var table = new TableDefinition
        {
            Schema = schema,
            Name = name,
            ObjectId = r.Int(0),
            Created = r.Date(1),
            Modified = r.Date(2),
            DatabaseCollation = r.NStr(3),
            RowCount = r.Long(4)
        };

        await r.NextResultAsync();
        while (await r.ReadAsync())
        {
            var isAlias = r.Int(5) == 1;
            var baseType = r.GetString(4);
            var typeSchema = r.NStr(3);
            table.Columns.Add(new ColumnDefinition
            {
                ColumnId = r.Int(0),
                Name = r.GetString(1),
                DataType = r.GetString(2),
                TypeSchema = isAlias && typeSchema != "sys" ? typeSchema : null,
                BaseType = baseType,
                Length = ScriptBuilder.FormatLength(r.GetString(2), isAlias, r.Int(6), r.Int(7), r.Int(8)),
                IsNullable = r.Int(9) == 1,
                IsIdentity = r.Int(10) == 1,
                IsComputed = r.Int(11) == 1,
                Collation = r.NStr(12),
                IsRowGuid = r.Int(13) == 1,
                IsSparse = r.Int(14) == 1,
                Seed = r.NStr(15) ?? "1",
                Increment = r.NStr(16) ?? "1",
                ComputedDefinition = r.NStr(17),
                IsPersisted = r.Int(18) == 1,
                DefaultName = r.NStr(19),
                DefaultDefinition = r.NStr(20)
            });
        }

        await r.NextResultAsync();
        var indexes = new Dictionary<int, IndexDefinition>();
        while (await r.ReadAsync())
        {
            var index = new IndexDefinition
            {
                IndexId = r.Int(0),
                Name = r.GetString(1),
                Type = r.Int(2),
                TypeDesc = r.GetString(3),
                IsUnique = r.Int(4) == 1,
                IsPrimaryKey = r.Int(5) == 1,
                IsUniqueConstraint = r.Int(6) == 1,
                IsDisabled = r.Int(7) == 1,
                FillFactor = r.Int(8),
                Filter = r.NStr(9)
            };
            indexes[index.IndexId] = index;
            table.Indexes.Add(index);
        }

        await r.NextResultAsync();
        while (await r.ReadAsync())
        {
            if (!indexes.TryGetValue(r.Int(0), out var index))
                continue;
            if (r.Int(3) == 1 || (r.Int(4) == 0 && index.Type is 5 or 6))
                index.Included.Add(r.GetString(1));
            else
                index.Columns.Add(new IndexColumn(r.GetString(1), r.Int(2) == 1));
        }

        await r.NextResultAsync();
        var foreignKeys = new Dictionary<int, ForeignKeyDefinition>();
        while (await r.ReadAsync())
        {
            var fk = new ForeignKeyDefinition
            {
                ObjectId = r.Int(0),
                Name = r.GetString(1),
                ReferencedSchema = r.GetString(2),
                ReferencedTable = r.GetString(3),
                DeleteAction = r.GetString(4),
                UpdateAction = r.GetString(5),
                IsDisabled = r.Int(6) == 1,
                IsNotTrusted = r.Int(7) == 1
            };
            foreignKeys[fk.ObjectId] = fk;
            table.ForeignKeys.Add(fk);
        }

        await r.NextResultAsync();
        while (await r.ReadAsync())
        {
            if (foreignKeys.TryGetValue(r.Int(0), out var fk))
            {
                fk.Columns.Add(r.GetString(1));
                fk.ReferencedColumns.Add(r.GetString(2));
            }
        }

        await r.NextResultAsync();
        while (await r.ReadAsync())
            table.Checks.Add(new CheckDefinition(r.GetString(0), r.GetString(1), r.Int(2) == 1));

        await r.DisposeAsync();

        // Needs VIEW DATABASE STATE, which not every login has
        try
        {
            await using var size = Db.Command(connection,
                "SELECT SUM(reserved_page_count) * 8 FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID(@name)",
                ("@name", Sql.Q(schema, name)));
            if (await size.ExecuteScalarAsync() is { } kb and not DBNull)
                table.ReservedKb = Convert.ToInt64(kb);
        }
        catch (SqlException)
        {
        }

        return table;
    }

    public async Task<List<string>> GetColumnNamesAsync(ManagedServer server, string database, string qualifiedName) =>
        await Db.QueryAsync(server.ConnectionStringFor(database),
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@name) ORDER BY column_id",
            r => r.GetString(0), ("@name", qualifiedName));

    public async Task<List<ParameterDefinition>> GetParametersAsync(ManagedServer server, string database, string schema, string name) =>
        await Db.QueryAsync(server.ConnectionStringFor(database), """
            SELECT p.parameter_id, p.name, tp.name, CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                   SCHEMA_NAME(tp.schema_id), p.max_length, p.precision, p.scale, CAST(p.is_output AS int), CAST(p.has_default_value AS int),
                   TYPE_NAME(p.system_type_id)
            FROM sys.parameters p
            JOIN sys.types tp ON tp.user_type_id = p.user_type_id
            WHERE p.object_id = OBJECT_ID(@name)
            ORDER BY p.parameter_id
            """,
            r =>
            {
                var isAlias = r.Int(3) == 1;
                var typeSchema = r.NStr(4);
                return new ParameterDefinition(
                    r.Int(0), r.Str(1),
                    ScriptBuilder.FormatType(r.GetString(2), isAlias, typeSchema == "sys" ? null : typeSchema, r.Int(5), r.Int(6), r.Int(7)),
                    r.Str(10), r.Int(8) == 1, r.Int(9) == 1);
            },
            ("@name", Sql.Q(schema, name)));

    /// <summary>CREATE text of a view, procedure, function or trigger, with its SET options like SSMS scripts it.</summary>
    public async Task<string> GetModuleScriptAsync(ManagedServer server, string database, string qualifiedName)
    {
        var rows = await Db.QueryAsync(server.ConnectionStringFor(database), """
            SELECT m.definition, CAST(m.uses_ansi_nulls AS int), CAST(m.uses_quoted_identifier AS int)
            FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(@name)
            """,
            r => (Definition: r.NStr(0), AnsiNulls: r.Int(1) == 1, QuotedIdentifier: r.Int(2) == 1),
            ("@name", qualifiedName));

        if (rows.Count == 0)
            throw new InvalidOperationException($"{qualifiedName} was not found.");
        if (rows[0].Definition is null)
            throw new InvalidOperationException($"{qualifiedName} is encrypted (WITH ENCRYPTION) and can't be scripted.");

        return $"SET ANSI_NULLS {(rows[0].AnsiNulls ? "ON" : "OFF")}\nGO\nSET QUOTED_IDENTIFIER {(rows[0].QuotedIdentifier ? "ON" : "OFF")}\nGO\n{rows[0].Definition!.Trim()}\nGO\n";
    }

    /// <summary>"Script as CREATE" for any scriptable node.</summary>
    public async Task<string> ScriptCreateAsync(ExplorerNode node)
    {
        var server = node.Server;
        var db = node.Database ?? "master";
        var cs = server.ConnectionStringFor(db);

        switch (node.Kind)
        {
            case NodeKind.Table:
                return ScriptBuilder.CreateTable(await GetTableAsync(server, db, node.Schema!, node.Name!));

            case NodeKind.View or NodeKind.Procedure or NodeKind.Function:
                return await GetModuleScriptAsync(server, db, node.QualifiedName);

            case NodeKind.Trigger:
                return await GetModuleScriptAsync(server, db, Sql.Q(node.ParentSchema!, node.Name!));

            case NodeKind.Index or NodeKind.Key or NodeKind.Constraint:
            {
                var table = await GetTableAsync(server, db, node.ParentSchema!, node.ParentName!);
                if (table.Indexes.FirstOrDefault(i => i.Name == node.Name) is { } index)
                    return ScriptBuilder.CreateIndex(table.FullName, index) + "\nGO\n";
                if (table.ForeignKeys.FirstOrDefault(f => f.Name == node.Name) is { } fk)
                    return ScriptBuilder.AddForeignKey(table.FullName, fk) + "\nGO\n";
                if (table.Checks.FirstOrDefault(c => c.Name == node.Name) is { } check)
                    return ScriptBuilder.AddCheck(table.FullName, check) + "\nGO\n";
                if (table.Columns.FirstOrDefault(c => c.DefaultName == node.Name) is { } column)
                    return ScriptBuilder.AddDefault(table.FullName, column.DefaultName!, column.DefaultDefinition!, column.Name) + "\nGO\n";
                throw new InvalidOperationException($"{node.Name} was not found.");
            }

            case NodeKind.Synonym:
            {
                var target = await Db.ScalarAsync(cs, "SELECT base_object_name FROM sys.synonyms WHERE object_id = OBJECT_ID(@name)", ("@name", node.QualifiedName));
                return $"CREATE SYNONYM {node.QualifiedName} FOR {target};\nGO\n";
            }

            case NodeKind.Sequence:
            {
                var rows = await Db.QueryAsync(cs, """
                    SELECT TYPE_NAME(user_type_id), CAST(start_value AS nvarchar(50)), CAST(increment AS nvarchar(50)),
                           CAST(minimum_value AS nvarchar(50)), CAST(maximum_value AS nvarchar(50)), CAST(is_cycling AS int),
                           CAST(is_cached AS int), cache_size
                    FROM sys.sequences WHERE object_id = OBJECT_ID(@name)
                    """,
                    r => $"CREATE SEQUENCE {node.QualifiedName}\n    AS {r.GetString(0)}\n    START WITH {r.Str(1)}\n    INCREMENT BY {r.Str(2)}\n    MINVALUE {r.Str(3)}\n    MAXVALUE {r.Str(4)}\n    {(r.Int(5) == 1 ? "CYCLE" : "NO CYCLE")}\n    {(r.Int(6) == 1 ? (r.IsDBNull(7) ? "CACHE" : $"CACHE {r.Int(7)}") : "NO CACHE")};\nGO\n",
                    ("@name", node.QualifiedName));
                return rows.FirstOrDefault() ?? throw new InvalidOperationException($"{node.QualifiedName} was not found.");
            }

            case NodeKind.UserType:
                return await ScriptTypeAsync(server, db, node);

            case NodeKind.User:
            {
                var rows = await Db.QueryAsync(cs, """
                    SELECT dp.name, sp.name, dp.default_schema_name, dp.type
                    FROM sys.database_principals dp LEFT JOIN sys.server_principals sp ON sp.sid = dp.sid
                    WHERE dp.name = @name
                    """,
                    r => (Login: r.NStr(1), Schema: r.NStr(2), Type: r.Str(3).Trim()),
                    ("@name", node.Name));
                var user = rows.FirstOrDefault();
                var login = user.Login is null ? " WITHOUT LOGIN" : $" FOR LOGIN {Sql.Q(user.Login)}";
                var schema = user.Schema is null ? "" : $" WITH DEFAULT_SCHEMA = {Sql.Q(user.Schema)}";
                return $"CREATE USER {Sql.Q(node.Name)}{login}{schema};\nGO\n";
            }

            case NodeKind.Role:
            {
                var owner = await Db.ScalarAsync(cs, """
                    SELECT o.name FROM sys.database_principals r JOIN sys.database_principals o ON o.principal_id = r.owning_principal_id WHERE r.name = @name
                    """, ("@name", node.Name));
                return $"CREATE ROLE {Sql.Q(node.Name)}{(owner is string o ? $" AUTHORIZATION {Sql.Q(o)}" : "")};\nGO\n";
            }

            case NodeKind.Schema:
            {
                var owner = await Db.ScalarAsync(cs, """
                    SELECT p.name FROM sys.schemas s JOIN sys.database_principals p ON p.principal_id = s.principal_id WHERE s.name = @name
                    """, ("@name", node.Name));
                return $"CREATE SCHEMA {Sql.Q(node.Name)}{(owner is string o ? $" AUTHORIZATION {Sql.Q(o)}" : "")};\nGO\n";
            }

            case NodeKind.Login:
            {
                var rows = await Db.QueryAsync(server.Master, """
                    SELECT p.type, p.default_database_name, CAST(p.is_disabled AS int), CAST(ISNULL(l.is_policy_checked, 0) AS int), CAST(ISNULL(l.is_expiration_checked, 0) AS int)
                    FROM sys.server_principals p LEFT JOIN sys.sql_logins l ON l.principal_id = p.principal_id
                    WHERE p.name = @name
                    """,
                    r => (Type: r.Str(0).Trim(), Db: r.NStr(1), Disabled: r.Int(2) == 1, Policy: r.Int(3) == 1, Expiration: r.Int(4) == 1),
                    ("@name", node.Name));
                var login = rows.FirstOrDefault();
                var sb = new StringBuilder();
                if (login.Type == "S")
                    sb.Append($"CREATE LOGIN {Sql.Q(node.Name)} WITH PASSWORD = N'<password>', DEFAULT_DATABASE = {Sql.Q(login.Db ?? "master")}, CHECK_POLICY = {(login.Policy ? "ON" : "OFF")}, CHECK_EXPIRATION = {(login.Expiration ? "ON" : "OFF")};\n");
                else
                    sb.Append($"CREATE LOGIN {Sql.Q(node.Name)} FROM WINDOWS WITH DEFAULT_DATABASE = {Sql.Q(login.Db ?? "master")};\n");
                if (login.Disabled)
                    sb.Append($"ALTER LOGIN {Sql.Q(node.Name)} DISABLE;\n");
                return sb.Append("GO\n").ToString();
            }

            default:
                throw new NotSupportedException();
        }
    }

    private static async Task<string> ScriptTypeAsync(ManagedServer server, string db, ExplorerNode node)
    {
        var cs = server.ConnectionStringFor(db);
        if (node.SubType == "TT")
        {
            var columns = await Db.QueryAsync(cs, """
                SELECT c.name, tp.name, CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                       SCHEMA_NAME(tp.schema_id), c.max_length, c.precision, c.scale, CAST(c.is_nullable AS int)
                FROM sys.table_types tt
                JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                JOIN sys.types tp ON tp.user_type_id = c.user_type_id
                WHERE tt.user_type_id = TYPE_ID(@name)
                ORDER BY c.column_id
                """,
                r => $"    {Sql.Q(r.GetString(0))} {ScriptBuilder.FormatType(r.GetString(1), r.Int(2) == 1, r.NStr(3) == "sys" ? null : r.NStr(3), r.Int(4), r.Int(5), r.Int(6))} {(r.Int(7) == 1 ? "NULL" : "NOT NULL")}",
                ("@name", node.QualifiedName));
            return $"CREATE TYPE {node.QualifiedName} AS TABLE\n(\n{string.Join(",\n", columns)}\n);\nGO\n";
        }

        var rows = await Db.QueryAsync(cs, """
            SELECT TYPE_NAME(system_type_id), max_length, precision, scale, CAST(is_nullable AS int)
            FROM sys.types WHERE user_type_id = TYPE_ID(@name)
            """,
            r => $"CREATE TYPE {node.QualifiedName} FROM {ScriptBuilder.FormatType(r.GetString(0), false, null, r.Int(1), r.Int(2), r.Int(3))} {(r.Int(4) == 1 ? "NULL" : "NOT NULL")};\nGO\n",
            ("@name", node.QualifiedName));
        return rows.FirstOrDefault() ?? throw new InvalidOperationException($"{node.QualifiedName} was not found.");
    }

    /// <summary>Lists the objects Generate Scripts can include.</summary>
    public async Task<List<(string Type, string Schema, string Name)>> GetScriptableObjectsAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database), """
            SELECT o.type, s.name, o.name
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','FN','IF','TF','SN')
            ORDER BY CASE o.type WHEN 'U' THEN 0 WHEN 'V' THEN 1 WHEN 'P' THEN 2 WHEN 'SN' THEN 4 ELSE 3 END, s.name, o.name
            """,
            r => (r.GetString(0).Trim(), r.GetString(1), r.GetString(2)));

    /// <summary>
    /// Generate Scripts: schemas, tables, data, foreign keys, synonyms, then views/functions/procedures in creation order, then triggers.
    /// Foreign keys come after the data so the inserts don't depend on table order.
    /// </summary>
    public async Task<string> GenerateScriptAsync(ManagedServer server, string database, GenerateScriptOptions options, Action<string>? progress = null)
    {
        var cs = server.ConnectionStringFor(database);
        var sb = new StringBuilder();
        sb.Append($"-- Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} from {server.DisplayName} / {database}\n\n");
        if (options.UseDatabase)
            sb.Append($"USE {Sql.Q(database)};\nGO\n\n");

        bool Include(string schema, string name) =>
            options.OnlyObjects is null || options.OnlyObjects.Contains($"{schema}.{name}");

        var objects = await Db.QueryAsync(cs, """
            SELECT o.type, s.name, o.name, o.object_id, OBJECT_SCHEMA_NAME(o.parent_object_id), OBJECT_NAME(o.parent_object_id)
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','FN','IF','TF','SN','TR')
            ORDER BY o.object_id
            """,
            r => (Type: r.GetString(0).Trim(), Schema: r.GetString(1), Name: r.GetString(2), Id: r.Int(3), ParentSchema: r.NStr(4), ParentName: r.NStr(5)));

        var tables = options.Tables ? objects.Where(o => o.Type == "U" && Include(o.Schema, o.Name)).OrderBy(o => o.Schema).ThenBy(o => o.Name).ToList() : [];

        var schemas = objects.Where(o => o.Type != "TR" && Include(o.Schema, o.Name)).Select(o => o.Schema)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(s => !s.Equals("dbo", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var schema in schemas)
            sb.Append($"IF SCHEMA_ID({Sql.Lit(schema)}) IS NULL\n    EXEC(N'CREATE SCHEMA {Sql.Q(schema).Replace("'", "''")}');\nGO\n");
        if (schemas.Count > 0)
            sb.Append('\n');

        await using var connection = await Db.OpenAsync(cs);
        var definitions = new List<TableDefinition>();
        foreach (var t in tables)
        {
            progress?.Invoke($"{t.Schema}.{t.Name}");
            var table = await GetTableAsync(connection, server, t.Schema, t.Name);
            definitions.Add(table);
            if (options.DropFirst)
                sb.Append(ScriptBuilder.DropTable(table.FullName)).Append("\nGO\n");
            sb.Append($"-- Table {table.FullName}\n");
            sb.Append(ScriptBuilder.CreateTable(table, new ScriptBuilder.TableScriptOptions(ForeignKeys: false))).Append('\n');
        }

        if (options.Data)
        {
            foreach (var table in definitions.Where(t => t.RowCount > 0))
            {
                progress?.Invoke($"{table.Schema}.{table.Name} (data)");
                await AppendDataAsync(sb, connection, table, options.MaxDataRows);
            }
        }

        foreach (var table in definitions.Where(t => t.ForeignKeys.Count > 0))
            foreach (var fk in table.ForeignKeys)
                sb.Append(ScriptBuilder.AddForeignKey(table.FullName, fk)).Append("\nGO\n");

        if (options.Synonyms)
            foreach (var synonym in objects.Where(o => o.Type == "SN" && Include(o.Schema, o.Name)))
            {
                var target = await Db.ScalarAsync(cs, "SELECT base_object_name FROM sys.synonyms WHERE object_id = @id", ("@id", synonym.Id));
                sb.Append($"CREATE SYNONYM {Sql.Q(synonym.Schema, synonym.Name)} FOR {target};\nGO\n");
            }

        var modules = objects.Where(o =>
            (options.Views && o.Type == "V") ||
            (options.Procedures && o.Type == "P") ||
            (options.Functions && o.Type is "FN" or "IF" or "TF"))
            .Where(o => Include(o.Schema, o.Name));

        foreach (var module in modules)
        {
            progress?.Invoke($"{module.Schema}.{module.Name}");
            var name = Sql.Q(module.Schema, module.Name);
            if (options.DropFirst)
            {
                var keyword = module.Type switch { "V" => "VIEW", "P" => "PROCEDURE", _ => "FUNCTION" };
                sb.Append($"IF OBJECT_ID({Sql.Lit(name)}) IS NOT NULL\n    DROP {keyword} {name};\nGO\n");
            }

            try
            {
                sb.Append(await GetModuleScriptAsync(server, database, name)).Append('\n');
            }
            catch (InvalidOperationException ex)
            {
                sb.Append($"-- {ex.Message}\n\n");
            }
        }

        if (options.Triggers)
            foreach (var trigger in objects.Where(o => o.Type == "TR" && o.ParentName is not null && Include(o.ParentSchema!, o.ParentName!)))
            {
                try
                {
                    sb.Append(await GetModuleScriptAsync(server, database, Sql.Q(trigger.Schema, trigger.Name))).Append('\n');
                }
                catch (InvalidOperationException ex)
                {
                    sb.Append($"-- {ex.Message}\n\n");
                }
            }

        return sb.ToString();
    }

    private static async Task AppendDataAsync(StringBuilder sb, SqlConnection connection, TableDefinition table, int maxRows)
    {
        var columns = table.Columns.Where(c => !c.IsComputed && !c.IsRowVersion).ToList();
        if (columns.Count == 0)
            return;

        // CLR types are read as text and converted back by SQL Server on insert
        var select = string.Join(", ", columns.Select(c => c.BaseType is "geography" or "geometry" or "hierarchyid"
            ? $"{Sql.Q(c.Name)}.ToString()" : Sql.Q(c.Name)));
        var hasIdentity = columns.Any(c => c.IsIdentity);
        var columnList = string.Join(", ", columns.Select(c => Sql.Q(c.Name)));

        sb.Append($"-- Data {table.FullName}\n");
        if (hasIdentity)
            sb.Append($"SET IDENTITY_INSERT {table.FullName} ON;\n");

        await using var command = new SqlCommand($"SELECT TOP ({maxRows}) {select} FROM {table.FullName}", connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            var values = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                values[i] = SqlValues.Literal(reader.IsDBNull(i) ? null : reader.GetValue(i), columns[i].BaseType);
            sb.Append($"INSERT INTO {table.FullName} ({columnList}) VALUES ({string.Join(", ", values)});\n");
            if (++count % 500 == 0)
                sb.Append("GO\n");
        }

        if (hasIdentity)
            sb.Append($"SET IDENTITY_INSERT {table.FullName} OFF;\n");
        sb.Append("GO\n\n");
    }
}
