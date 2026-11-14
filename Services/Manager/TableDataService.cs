using System.Text;
using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

public sealed class RowSaveException(int rowIndex, string message, Exception? inner = null) : Exception(message, inner)
{
    public int RowIndex { get; } = rowIndex;
}

/// <summary>"Edit Top N Rows": loads rows with their key and saves inserts, updates and deletes in one transaction.</summary>
public sealed class TableDataService
{
    public async Task<EditData> LoadAsync(ManagedServer server, string database, string schema, string name, int top, string? where, string? orderBy)
    {
        var table = Sql.Q(schema, name);
        await using var connection = await Db.OpenAsync(server.ConnectionStringFor(database));

        var columns = new List<EditColumn>();
        await using (var command = Db.Command(connection, """
            SELECT c.name, TYPE_NAME(c.system_type_id), tp.name, CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                   c.max_length, c.precision, c.scale, CAST(c.is_nullable AS int), CAST(c.is_identity AS int), CAST(c.is_computed AS int),
                   CAST(CASE WHEN c.system_type_id = 240 THEN 1 ELSE 0 END AS int)
            FROM sys.columns c
            JOIN sys.types tp ON tp.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@name)
            ORDER BY c.column_id
            """, ("@name", table)))
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var isClr = r.Int(10) == 1;
                columns.Add(new EditColumn
                {
                    Name = r.GetString(0),
                    Type = isClr ? r.GetString(2).ToLowerInvariant() : r.GetString(1).ToLowerInvariant(),
                    TypeDisplay = ScriptBuilder.FormatType(r.GetString(2), r.Int(3) == 1, null, r.Int(4), r.Int(5), r.Int(6)),
                    IsNullable = r.Int(7) == 1,
                    IsIdentity = r.Int(8) == 1,
                    IsComputed = r.Int(9) == 1,
                    IsClrType = isClr
                });
            }
        }

        if (columns.Count == 0)
            throw new InvalidOperationException($"{table} was not found.");

        // Key: primary key, else the first unique index whose columns are all NOT NULL
        var keyColumns = new List<string>();
        await using (var command = Db.Command(connection, """
            SELECT TOP (1) WITH TIES c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@name) AND i.is_unique = 1 AND i.is_disabled = 0 AND i.type IN (1, 2)
              AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic2 JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                              WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.is_included_column = 0 AND c2.is_nullable = 1)
            ORDER BY CASE WHEN i.is_primary_key = 1 THEN 0 ELSE 1 END, i.index_id
            """, ("@name", table)))
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                keyColumns.Add(r.GetString(0));
        }

        var data = new EditData { Columns = columns };
        if (keyColumns.Count > 0)
        {
            foreach (var key in keyColumns)
            {
                var index = columns.FindIndex(c => c.Name == key);
                if (index >= 0)
                {
                    columns[index].IsKey = true;
                    data.KeyIndexes.Add(index);
                }
            }
        }
        else
        {
            data.MatchesAllColumns = true;
            for (var i = 0; i < columns.Count; i++)
                if (columns[i].IsComparable && !columns[i].IsComputed)
                    data.KeyIndexes.Add(i);
        }

        var select = string.Join(", ", columns.Select(c => c.IsClrType ? $"{Sql.Q(c.Name)}.ToString() AS {Sql.Q(c.Name)}" : Sql.Q(c.Name)));
        var sql = new StringBuilder($"SELECT TOP ({top}) {select}\nFROM {table}");
        if (!string.IsNullOrWhiteSpace(where))
            sql.Append("\nWHERE ").Append(where.Trim());
        if (!string.IsNullOrWhiteSpace(orderBy))
            sql.Append("\nORDER BY ").Append(orderBy.Trim());
        data.SelectSql = sql.ToString();

        await using (var command = new SqlCommand(data.SelectSql, connection) { CommandTimeout = 300 })
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var original = new object?[columns.Count];
                var values = new string?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    original[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                    values[i] = SqlValues.ToEditText(original[i], columns[i].Type);
                }

                data.Rows.Add(new EditRow { Original = original, Values = values, Changed = new bool[columns.Count] });
            }
        }

        return data;
    }

    public EditRow NewRow(EditData data) => new()
    {
        Original = new object?[data.Columns.Count],
        Values = new string?[data.Columns.Count],
        Changed = new bool[data.Columns.Count],
        State = EditRowState.Added
    };

    /// <summary>Saves every pending change in one transaction; on error nothing is saved.</summary>
    public async Task<int> SaveAsync(ManagedServer server, string database, string schema, string name, EditData data)
    {
        var table = Sql.Q(schema, name);
        await using var connection = await Db.OpenAsync(server.ConnectionStringFor(database));
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var saved = 0;

        for (var rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
        {
            var row = data.Rows[rowIndex];
            if (row.State == EditRowState.Unchanged || (row.State == EditRowState.Deleted && row.Original.All(v => v is null) && row.Changed.All(c => !c)))
                continue;

            try
            {
                await using var command = new SqlCommand { Connection = connection, Transaction = transaction, CommandTimeout = 120 };
                switch (row.State)
                {
                    case EditRowState.Added:
                        BuildInsert(command, table, data, row);
                        await command.ExecuteNonQueryAsync();
                        break;

                    case EditRowState.Modified:
                        if (!BuildUpdate(command, table, data, row))
                            continue;
                        await ExpectOneRowAsync(command);
                        break;

                    case EditRowState.Deleted:
                        BuildDelete(command, table, data, row);
                        await ExpectOneRowAsync(command);
                        break;
                }

                saved++;
            }
            catch (Exception ex) when (ex is SqlException or FormatException or OverflowException or InvalidOperationException)
            {
                await transaction.RollbackAsync();
                throw new RowSaveException(rowIndex, ex.Message, ex);
            }
        }

        await transaction.CommitAsync();
        return saved;
    }

    private static async Task ExpectOneRowAsync(SqlCommand command)
    {
        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
            throw new InvalidOperationException("The row was not found. It may have been changed or deleted by someone else; refresh and try again.");
    }

    private static object ParseCell(EditData data, EditRow row, int i)
    {
        var column = data.Columns[i];
        try
        {
            return SqlValues.Parse(row.Values[i], column.Type, column.IsText);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FormatException($"Invalid value \"{row.Values[i]}\" for column {column.Name} ({column.TypeDisplay}).", ex);
        }
    }

    private static string ParamType(EditColumn column) => column.IsClrType ? "nvarchar" : column.Type;

    private static void BuildInsert(SqlCommand command, string table, EditData data, EditRow row)
    {
        var names = new List<string>();
        var values = new List<string>();
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var column = data.Columns[i];
            // Untouched cells are left out so defaults and NULL apply
            if (column.IsReadOnly || !row.Changed[i])
                continue;
            names.Add(Sql.Q(column.Name));
            values.Add($"@v{i}");
            command.Parameters.Add(SqlValues.Parameter($"@v{i}", ParseCell(data, row, i), ParamType(column)));
        }

        command.CommandText = names.Count == 0
            ? $"INSERT INTO {table} DEFAULT VALUES;"
            : $"INSERT INTO {table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", values)});";
    }

    private static bool BuildUpdate(SqlCommand command, string table, EditData data, EditRow row)
    {
        var sets = new List<string>();
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var column = data.Columns[i];
            if (column.IsReadOnly || !row.Changed[i])
                continue;
            sets.Add($"{Sql.Q(column.Name)} = @v{i}");
            command.Parameters.Add(SqlValues.Parameter($"@v{i}", ParseCell(data, row, i), ParamType(column)));
        }

        if (sets.Count == 0)
            return false;

        command.CommandText = $"UPDATE {Top(data)}{table} SET {string.Join(", ", sets)} WHERE {KeyCondition(command, data, row)};";
        return true;
    }

    private static void BuildDelete(SqlCommand command, string table, EditData data, EditRow row) =>
        command.CommandText = $"DELETE {Top(data)}FROM {table} WHERE {KeyCondition(command, data, row)};";

    /// <summary>Without a key, duplicate rows are identical, so changing any one of them is the same.</summary>
    private static string Top(EditData data) => data.MatchesAllColumns ? "TOP (1) " : string.Empty;

    private static string KeyCondition(SqlCommand command, EditData data, EditRow row)
    {
        var conditions = new List<string>();
        foreach (var i in data.KeyIndexes)
        {
            var column = data.Columns[i];
            if (row.Original[i] is null)
            {
                conditions.Add($"{Sql.Q(column.Name)} IS NULL");
                continue;
            }

            conditions.Add($"{Sql.Q(column.Name)} = @k{i}");
            command.Parameters.Add(SqlValues.Parameter($"@k{i}", row.Original[i]!, column.Type));
        }

        return string.Join(" AND ", conditions);
    }
}
