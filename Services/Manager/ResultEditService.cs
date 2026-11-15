using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>
/// Edits cells of a query's results. SQL Server describes the query in browse mode
/// (sys.dm_exec_describe_first_result_set), which gives each column's base table and that table's key columns,
/// so a changed cell can be saved with an UPDATE ... WHERE key = value. Nothing in the query is run again.
/// </summary>
public sealed class ResultEditService
{
    private const string DescribeQuery = """
        SELECT column_ordinal, CAST(is_hidden AS int), source_database, source_schema, source_table, source_column,
               CAST(ISNULL(is_identity_column, 0) AS int), CAST(ISNULL(is_part_of_unique_key, 0) AS int),
               CAST(ISNULL(is_updateable, 0) AS int), CAST(ISNULL(is_computed_column, 0) AS int),
               system_type_name, CAST(ISNULL(is_nullable, 1) AS int), error_message
        FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 1)
        ORDER BY column_ordinal
        """;

    /// <summary>Primary key, unique, foreign key, identity, computed and nullability of every column of a table.</summary>
    private const string KeysQuery = """
        SELECT c.name,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                                      WHERE i.object_id = c.object_id AND i.is_primary_key = 1 AND ic.column_id = c.column_id) THEN 1 ELSE 0 END AS int),
               CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                                      WHERE i.object_id = c.object_id AND i.is_unique = 1 AND i.is_primary_key = 0
                                        AND ic.is_included_column = 0 AND ic.column_id = c.column_id) THEN 1 ELSE 0 END AS int),
               fk.ref_schema, fk.ref_table, fk.ref_column,
               CAST(c.is_identity AS int), CAST(c.is_computed AS int), CAST(c.is_nullable AS int)
        FROM sys.columns c
        OUTER APPLY (
            SELECT TOP (1) SCHEMA_NAME(rt.schema_id) AS ref_schema, rt.name AS ref_table, rc.name AS ref_column
            FROM sys.foreign_key_columns fkc
            JOIN sys.objects rt ON rt.object_id = fkc.referenced_object_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
        ) fk
        WHERE c.object_id = OBJECT_ID(@table)
        """;

    private sealed record ColumnKeys(bool PrimaryKey, bool Unique, (string, string, string)? ForeignKey, bool Identity, bool Computed, bool Nullable);

    private sealed record Described(
        int Ordinal, bool Hidden, string? Database, string? Schema, string? Table, string? Column,
        bool Identity, bool Key, bool Updateable, bool Computed, string Type, bool Nullable);

    public Task<ResultEditInfo> DescribeAsync(ManagedServer server, ResultSet set) =>
        set.EditInfo ??= server.IsSqlServer ? DescribeCoreAsync(server, set) : DescribeOtherAsync(server, set);

    /// <summary>
    /// MySQL / PostgreSQL / Oracle: the provider reports each result column's base table and column;
    /// the table's keys come from the engine's catalog.
    /// </summary>
    private static async Task<ResultEditInfo> DescribeOtherAsync(ManagedServer server, ResultSet set)
    {
        var dialect = Dialects.For(server);
        if (set.Origins is not { } origins || origins.Count != set.Columns.Count || origins.All(o => o is null))
            return ResultEditInfo.No("NotEditableNoTable");

        var tables = new Dictionary<string, List<ColumnKeysInfo>>(StringComparer.OrdinalIgnoreCase);
        var info = new Dictionary<int, ResultColumnInfo>();
        var targets = new Dictionary<int, CellEditTarget>();
        var missingKey = false;

        // MySQL tables live in a database; PostgreSQL and Oracle in a schema of the current database
        string? SchemaOf(ColumnOrigin origin) => origin.Schema ?? (server.Engine == DatabaseEngine.PostgreSql ? "public" : set.Database);

        for (var i = 0; i < origins.Count; i++)
        {
            if (origins[i] is not { } origin)
                continue;

            var schema = SchemaOf(origin);
            var key = $"{schema}.{origin.Table}";
            if (!tables.TryGetValue(key, out var columns))
            {
                try
                {
                    columns = await dialect.LoadColumnsAsync(server, server.Engine == DatabaseEngine.PostgreSql ? set.Database : null, schema, origin.Table);
                }
                catch (Exception)
                {
                    columns = [];
                }

                tables[key] = columns;
            }

            var column = columns.FirstOrDefault(c => string.Equals(c.Name, origin.Column, StringComparison.OrdinalIgnoreCase));
            info[i] = new ResultColumnInfo(
                set.Database ?? string.Empty, schema ?? string.Empty, origin.Table, origin.Column,
                column?.PrimaryKey ?? false, column?.Unique ?? false, column?.ForeignKey,
                column?.Identity ?? false, column?.Computed ?? false, column?.Nullable ?? true,
                column?.Type ?? set.Columns[i].TypeName);

            if (column is null || column.Identity || column.Computed)
                continue;

            var primaryKey = columns.Where(c => c.PrimaryKey).ToList();
            var keys = primaryKey.Select(k => (Index: origins.FindIndex(o => o is not null &&
                    string.Equals(SchemaOf(o), schema, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.Table, origin.Table, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.Column, k.Name, StringComparison.OrdinalIgnoreCase)), Column: k.Name, k.Type))
                .ToList();
            if (keys.Count == 0 || keys.Any(k => k.Index < 0))
            {
                missingKey = true;
                continue;
            }

            targets[i] = new CellEditTarget(set.Database ?? string.Empty, dialect.Name(schema, origin.Table), column.Name, column.Type,
                true, column.Nullable, keys);
        }

        if (targets.Count == 0)
            return ResultEditInfo.No(missingKey ? "NotEditableNoKey" : "NotEditableNoTable") with { Info = info };
        return new ResultEditInfo(targets, null) { Info = info };
    }

    /// <summary>Saves one cell on MySQL / PostgreSQL / Oracle.</summary>
    private static async Task<string?> UpdateOtherAsync(ManagedServer server, CellEditTarget target, string?[] row, string? text)
    {
        var dialect = Dialects.For(server);
        var value = dialect.ToValue(text, target.Type);
        var args = new List<(string, object?)> { ("v", value) };
        var conditions = new List<string>();
        for (var i = 0; i < target.Keys.Count; i++)
        {
            var (index, column, type) = target.Keys[i];
            if (row[index] is not { } keyText)
            {
                conditions.Add($"{dialect.Q(column)} IS NULL");
                continue;
            }

            conditions.Add($"{dialect.Q(column)} = {dialect.P($"k{i}")}");
            args.Add(($"k{i}", dialect.ToValue(keyText, type, nullWord: false)));
        }

        // PostgreSQL connects to the table's database; MySQL and Oracle name the table fully
        var database = server.Engine == DatabaseEngine.PostgreSql ? target.Database : null;
        await using var connection = await dialect.OpenAsync(server, database);
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = dialect.Command(connection,
            $"UPDATE {target.Table} SET {dialect.Q(target.Column)} = {dialect.P("v")} WHERE {string.Join(" AND ", conditions)}",
            [.. args]);
        command.Transaction = transaction;
        command.CommandTimeout = 30;

        var affected = await command.ExecuteNonQueryAsync();
        if (affected != 1)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException(affected == 0
                ? "The row was not found. It may have been changed or deleted; run the query again."
                : $"The key matched {affected} rows, so nothing was changed.");
        }

        await transaction.CommitAsync();
        return value is DBNull ? null : SqlValues.Display(value, target.Type.ToLowerInvariant());
    }

    private static async Task<ResultEditInfo> DescribeCoreAsync(ManagedServer server, ResultSet set)
    {
        // The DMV exists from SQL Server 2012
        if (server.Major < 11)
            return ResultEditInfo.No("NotEditableVersion");
        if (set.BatchSql is null || set.Database is null)
            return ResultEditInfo.No("NotEditableUnknown");
        // It describes the first result set of a batch only
        if (set.IndexInBatch > 0)
            return ResultEditInfo.No("NotEditableNotFirst");

        List<Described> rows;
        string? error = null;
        try
        {
            rows = await Db.QueryAsync(server.ConnectionStringFor(set.Database), DescribeQuery, r =>
                {
                    error ??= r.NStr(12);
                    return new Described(
                        r.Int(0), r.Int(1) == 1, r.NStr(2), r.NStr(3), r.NStr(4), r.NStr(5),
                        r.Int(6) == 1, r.Int(7) == 1, r.Int(8) == 1, r.Int(9) == 1,
                        r.NStr(10) ?? string.Empty, r.Int(11) == 1);
                },
                ("@sql", set.BatchSql));
        }
        catch (SqlException ex)
        {
            return ResultEditInfo.No("NotEditableDescribe", ex.Message);
        }

        if (error is not null)
            return ResultEditInfo.No("NotEditableDescribe", error);

        var visible = rows.Where(r => !r.Hidden).ToList();
        if (visible.Count != set.Columns.Count)
            return ResultEditInfo.No("NotEditableShape");

        var info = await LoadColumnInfoAsync(server, set.Database, visible);
        var targets = new Dictionary<int, CellEditTarget>();
        var missingKey = false;
        for (var i = 0; i < visible.Count; i++)
        {
            var column = visible[i];
            if (column.Table is null || column.Column is null || !column.Updateable || column.Identity || column.Computed)
                continue;

            var database = column.Database ?? set.Database;
            bool SameTable(Described r) =>
                string.Equals(r.Table, column.Table, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Schema, column.Schema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Database ?? set.Database, database, StringComparison.OrdinalIgnoreCase);

            var keyColumns = rows.Where(r => r.Key && SameTable(r)).ToList();
            if (keyColumns.Count == 0 || keyColumns.Any(k => k.Hidden))
            {
                missingKey = true;
                continue;
            }

            var keys = keyColumns
                .Select(k => (Index: visible.IndexOf(k), Column: k.Column!, Type: BaseType(k.Type)))
                .GroupBy(k => k.Column, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var type = BaseType(column.Type);
            if (type is "geography" or "geometry" or "hierarchyid" or "timestamp" or "rowversion")
                continue;

            targets[i] = new CellEditTarget(
                database, Sql.Q(column.Schema ?? "dbo", column.Table), column.Column, type,
                type is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml",
                column.Nullable, keys);
        }

        if (targets.Count == 0)
            return ResultEditInfo.No(missingKey ? "NotEditableNoKey" : "NotEditableNoTable") with { Info = info };
        return new ResultEditInfo(targets, null) { Info = info };
    }

    /// <summary>Keys of every result column that comes from a table, read once per table.</summary>
    private static async Task<Dictionary<int, ResultColumnInfo>> LoadColumnInfoAsync(ManagedServer server, string defaultDatabase, List<Described> visible)
    {
        var tables = new Dictionary<string, Dictionary<string, ColumnKeys>>(StringComparer.OrdinalIgnoreCase);
        var info = new Dictionary<int, ResultColumnInfo>();

        for (var i = 0; i < visible.Count; i++)
        {
            var column = visible[i];
            if (column.Table is null || column.Column is null)
                continue;

            var database = column.Database ?? defaultDatabase;
            var schema = column.Schema ?? "dbo";
            var key = $"{database}|{schema}|{column.Table}";
            if (!tables.TryGetValue(key, out var columns))
            {
                columns = new Dictionary<string, ColumnKeys>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var keys = await Db.QueryAsync(server.ConnectionStringFor(database), KeysQuery,
                        r => (Name: r.GetString(0), Keys: new ColumnKeys(
                            r.Int(1) == 1, r.Int(2) == 1,
                            r.IsDBNull(3) ? null : (r.GetString(3), r.GetString(4), r.GetString(5)),
                            r.Int(6) == 1, r.Int(7) == 1, r.Int(8) == 1)),
                        ("@table", Sql.Q(schema, column.Table)));
                    foreach (var (name, flags) in keys)
                        columns[name] = flags;
                }
                catch (SqlException)
                {
                    // No access to the table's metadata: show the column without keys
                }

                tables[key] = columns;
            }

            var found = columns.GetValueOrDefault(column.Column);
            info[i] = new ResultColumnInfo(
                database, schema, column.Table, column.Column,
                found?.PrimaryKey ?? false, found?.Unique ?? false, found?.ForeignKey,
                found?.Identity ?? column.Identity, found?.Computed ?? column.Computed, found?.Nullable ?? column.Nullable,
                column.Type);
        }

        return info;
    }

    /// <summary>"nvarchar(100)" → "nvarchar".</summary>
    private static string BaseType(string typeName)
    {
        var paren = typeName.IndexOf('(');
        return (paren < 0 ? typeName : typeName[..paren]).Trim().ToLowerInvariant();
    }

    /// <summary>Saves one cell. Returns the value as the grid shows it (null = NULL).</summary>
    /// <exception cref="FormatException">The text doesn't fit the column type.</exception>
    /// <exception cref="InvalidOperationException">The row was not found, or the key matched more than one row.</exception>
    public async Task<string?> UpdateAsync(ManagedServer server, CellEditTarget target, string?[] row, string? text)
    {
        if (!server.IsSqlServer)
            return await UpdateOtherAsync(server, target, row, text);

        object value;
        try
        {
            value = SqlValues.Parse(text, target.Type, target.IsText);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FormatException($"\"{text}\" is not a valid {target.Type} value.", ex);
        }

        await using var connection = await Db.OpenAsync(server.ConnectionStringFor(target.Database));
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand { Connection = connection, Transaction = transaction, CommandTimeout = 30 };
        command.Parameters.Add(SqlValues.Parameter("@value", value, target.Type));

        var conditions = new List<string>();
        for (var i = 0; i < target.Keys.Count; i++)
        {
            var (index, column, type) = target.Keys[i];
            var keyText = row[index];
            if (keyText is null)
            {
                conditions.Add($"{Sql.Q(column)} IS NULL");
                continue;
            }

            if (keyText.EndsWith('…'))
                throw new InvalidOperationException($"The key column {column} is too long to find the row.");
            conditions.Add($"{Sql.Q(column)} = @k{i}");
            command.Parameters.Add(SqlValues.Parameter($"@k{i}", SqlValues.Parse(keyText, type, isText: true, nullWord: false), type));
        }

        command.CommandText = $"SET LOCK_TIMEOUT 15000;\nUPDATE {target.Table} SET {Sql.Q(target.Column)} = @value\nWHERE {string.Join(" AND ", conditions)};\nSELECT @@ROWCOUNT;";
        var affected = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (affected != 1)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException(affected == 0
                ? "The row was not found. It may have been changed or deleted; run the query again."
                : $"The key matched {affected} rows, so nothing was changed.");
        }

        await transaction.CommitAsync();
        return value is DBNull ? null : SqlValues.Display(value, target.Type);
    }
}
