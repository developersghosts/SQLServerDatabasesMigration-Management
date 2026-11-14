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

    private sealed record Described(
        int Ordinal, bool Hidden, string? Database, string? Schema, string? Table, string? Column,
        bool Identity, bool Key, bool Updateable, bool Computed, string Type, bool Nullable);

    public Task<ResultEditInfo> DescribeAsync(ManagedServer server, ResultSet set) =>
        set.EditInfo ??= DescribeCoreAsync(server, set);

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
            return ResultEditInfo.No(missingKey ? "NotEditableNoKey" : "NotEditableNoTable");
        return new ResultEditInfo(targets, null);
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
