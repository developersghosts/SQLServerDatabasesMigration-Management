using System.Globalization;
using Microsoft.Data.SqlClient;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>T-SQL quoting helpers.</summary>
public static class Sql
{
    /// <summary>Same as QUOTENAME().</summary>
    public static string Q(string? identifier) => "[" + (identifier ?? string.Empty).Replace("]", "]]") + "]";

    public static string Q(string schema, string name) => Q(schema) + "." + Q(name);

    public static string Name(string? schema, string? name) => schema is null ? Q(name) : Q(schema, name ?? string.Empty);

    /// <summary>Unicode string literal: N'...'.</summary>
    public static string Lit(string? value) => value is null ? "NULL" : "N'" + value.Replace("'", "''") + "'";
}

/// <summary>Short-lived metadata queries for the database manager.</summary>
internal static class Db
{
    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static SqlCommand Command(SqlConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static Task<List<T>> QueryAsync<T>(string connectionString, string sql, Func<SqlDataReader, T> map, params (string Name, object? Value)[] args) =>
        WithRetryAsync(connectionString, async () =>
        {
            await using var connection = await OpenAsync(connectionString);
            await using var command = Command(connection, sql, args);
            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<T>();
            while (await reader.ReadAsync())
                list.Add(map(reader));
            return list;
        });

    public static Task<object?> ScalarAsync(string connectionString, string sql, params (string Name, object? Value)[] args) =>
        WithRetryAsync(connectionString, async () =>
        {
            await using var connection = await OpenAsync(connectionString);
            await using var command = Command(connection, sql, args);
            var value = await command.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        });

    /// <summary>
    /// Pooled connections die when a database is taken offline, dropped or set to single user;
    /// the first query after that fails, so clear the pool and try once more.
    /// </summary>
    public static async Task<T> WithRetryAsync<T>(string connectionString, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (IsBrokenConnection(ex))
        {
            ClearPool(connectionString);
            return await action();
        }
    }

    public static void ClearPool(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        SqlConnection.ClearPool(connection);
    }

    private static bool IsBrokenConnection(SqlException ex) =>
        ex.Class >= 20 || ex.Number is 233 or 10053 or 10054 or 64 or 596 ||
        ex.Message.Contains("transport-level", StringComparison.OrdinalIgnoreCase);
}

internal static class ReaderExtensions
{
    public static string Str(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;

    public static string? NStr(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static int Int(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static long Long(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? 0 : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static decimal Dec(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? 0 : Convert.ToDecimal(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static bool Bool(this SqlDataReader reader, int i) =>
        !reader.IsDBNull(i) && Convert.ToBoolean(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static DateTime? Date(this SqlDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : Convert.ToDateTime(reader.GetValue(i), CultureInfo.InvariantCulture);

    public static string? NStr(this SqlDataReader reader, string column) => reader.NStr(reader.GetOrdinal(column));
    public static long Long(this SqlDataReader reader, string column) => reader.Long(reader.GetOrdinal(column));
    public static int Int(this SqlDataReader reader, string column) => reader.Int(reader.GetOrdinal(column));
    public static DateTime? Date(this SqlDataReader reader, string column) => reader.Date(reader.GetOrdinal(column));
}
