using System.Data.Common;
using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models;

namespace MigrateSQLServerDatabases.Services;

public sealed class MissingDatabaseException()
    : ArgumentException("The connection string must specify a database (Database / Initial Catalog).");

public sealed class SqlServerService
{
    private const string ServerInfoQuery = """
        SELECT
            CAST(@@SERVERNAME AS nvarchar(256)),
            DB_NAME(),
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
            CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128)),
            CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
            CAST(SERVERPROPERTY('EngineEdition') AS int)
        """;

    // sys.tables / sys.partitions are available from SQL Server 2005 onwards
    private const string TablesQuery = """
        SELECT s.name, t.name, ISNULL(SUM(p.rows), 0)
        FROM sys.tables t
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
        WHERE t.is_ms_shipped = 0
        GROUP BY s.name, t.name
        ORDER BY s.name, t.name
        """;

    private const string ColumnsQuery = """
        SELECT QUOTENAME(s.name) + '.' + QUOTENAME(t.name), c.name, TYPE_NAME(c.user_type_id),
               c.max_length, c.precision, c.scale, c.is_nullable,
               CAST(CASE WHEN c.is_identity = 1 OR c.is_computed = 1 OR c.default_object_id <> 0
                         OR c.system_type_id = 189 {0} THEN 1 ELSE 0 END AS bit),
               c.collation_name,
               CAST(COLLATIONPROPERTY(c.collation_name, 'CodePage') AS int),
               TYPE_NAME(c.system_type_id)
        FROM sys.columns c
        INNER JOIN sys.tables t ON t.object_id = c.object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0
        ORDER BY 1, c.column_id
        """;

    /// <summary>
    /// Validates the connection string and relaxes encryption defaults when the user did not
    /// specify them, so older SQL Server versions without a trusted certificate can still connect.
    /// </summary>
    /// <param name="requireDatabase">The migration needs a database; the database manager connects to the whole server.</param>
    public static string Normalize(string connectionString, bool requireDatabase = true)
    {
        var raw = new DbConnectionStringBuilder { ConnectionString = connectionString };

        // OLE DB strings (e.g. copied from a .udl file) start with Provider=..., which SqlClient rejects
        raw.Remove("Provider");

        var builder = new SqlConnectionStringBuilder(raw.ConnectionString);

        if (requireDatabase && string.IsNullOrWhiteSpace(builder.InitialCatalog))
            throw new MissingDatabaseException();

        if (!raw.ContainsKey("Encrypt"))
            builder.Encrypt = SqlConnectionEncryptOption.Optional;

        if (!raw.ContainsKey("TrustServerCertificate") && !raw.ContainsKey("Trust Server Certificate"))
            builder.TrustServerCertificate = true;

        if (!raw.ContainsKey("Connect Timeout") && !raw.ContainsKey("Connection Timeout") && !raw.ContainsKey("Timeout"))
            builder.ConnectTimeout = 15;

        return builder.ConnectionString;
    }

    public async Task<ServerInfo> GetServerInfoAsync(string connectionString, CancellationToken cancellationToken = default, bool requireDatabase = true)
    {
        await using var connection = new SqlConnection(Normalize(connectionString, requireDatabase));
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(ServerInfoQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new ServerInfo(
            ServerName: reader.IsDBNull(0) ? connection.DataSource : reader.GetString(0),
            DatabaseName: reader.GetString(1),
            ProductVersion: reader.GetString(2),
            ProductLevel: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Edition: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            EngineEdition: reader.IsDBNull(5) ? 0 : reader.GetInt32(5));
    }

    public async Task<List<TableInfo>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(Normalize(connectionString));
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(TablesQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tables = new List<TableInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new TableInfo(reader.GetString(0), reader.GetString(1), Convert.ToInt64(reader.GetValue(2))));
        }

        return tables;
    }

    /// <summary>
    /// Lists the column differences of the given source → destination table pairs (full names).
    /// Tables without differences are left out; collation-only differences that convert without
    /// loss are only counted, since a database with a different default collation would list every text column.
    /// </summary>
    public async Task<ColumnComparison> CompareColumnsAsync(
        string sourceConnectionString, string destinationConnectionString,
        IEnumerable<(string Source, string Destination)> tables,
        CancellationToken cancellationToken = default)
    {
        var sourceTask = GetColumnsAsync(sourceConnectionString, cancellationToken);
        var destinationTask = GetColumnsAsync(destinationConnectionString, cancellationToken);
        await Task.WhenAll(sourceTask, destinationTask);

        var differences = new List<TableColumnDiff>();
        var collationChanges = 0;
        var collationPairs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (table, destinationTable) in tables)
        {
            if (!sourceTask.Result.TryGetValue(table, out var sourceColumns) ||
                !destinationTask.Result.TryGetValue(destinationTable, out var destinationColumns))
                continue;

            var sourceByName = ToMap(sourceColumns);
            var destinationByName = ToMap(destinationColumns);
            var columns = new List<ColumnDiff>();

            foreach (var destination in destinationColumns)
            {
                if (!sourceByName.TryGetValue(destination.Name, out var source))
                {
                    columns.Add(new ColumnDiff(destination.Name, ColumnDiffKind.DestinationOnly, null, destination));
                }
                else if (!string.Equals(source.TypeDisplay, destination.TypeDisplay, StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(new ColumnDiff(destination.Name, ColumnDiffKind.TypeMismatch, source, destination));
                }
                else if (source.IsText && destination.IsText &&
                         !string.Equals(source.Collation, destination.Collation, StringComparison.OrdinalIgnoreCase))
                {
                    var diff = new ColumnDiff(destination.Name, ColumnDiffKind.CollationMismatch, source, destination);
                    if (diff.MayLoseCharacters)
                    {
                        columns.Add(diff);
                    }
                    else
                    {
                        collationChanges++;
                        collationPairs.Add($"{source.Collation} → {destination.Collation}");
                    }
                }
            }

            foreach (var source in sourceColumns.Where(c => !destinationByName.ContainsKey(c.Name)))
                columns.Add(new ColumnDiff(source.Name, ColumnDiffKind.SourceOnly, source, null));

            if (columns.Count > 0)
                differences.Add(new TableColumnDiff(table, destinationTable, columns));
        }

        return new ColumnComparison(differences, collationChanges, collationPairs.ToList());
    }

    private async Task<Dictionary<string, List<ColumnInfo>>> GetColumnsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(Normalize(connectionString));
        await connection.OpenAsync(cancellationToken);

        // generated_always_type (temporal period columns) only exists from SQL Server 2016
        await using var check = new SqlCommand("SELECT COL_LENGTH('sys.columns', 'generated_always_type')", connection);
        var hasGeneratedAlways = await check.ExecuteScalarAsync(cancellationToken) is not DBNull;

        var sql = string.Format(ColumnsQuery, hasGeneratedAlways ? "OR c.generated_always_type <> 0" : string.Empty);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (!columns.TryGetValue(table, out var list))
                columns[table] = list = [];

            var baseType = reader.GetString(10);
            list.Add(new ColumnInfo(
                Name: reader.GetString(1),
                Type: FormatType(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)),
                IsNullable: reader.GetBoolean(6),
                IsAutoFilled: reader.GetBoolean(7),
                Collation: reader.IsDBNull(8) ? null : reader.GetString(8),
                CodePage: reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                IsUnicode: baseType is "nchar" or "nvarchar" or "ntext"));
        }

        return columns;
    }

    private static string FormatType(string type, short maxLength, byte precision, byte scale) => type.ToLowerInvariant() switch
    {
        "nvarchar" or "nchar" => $"{type}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength == -1 ? "max" : maxLength.ToString())})",
        "decimal" or "numeric" => $"{type}({precision},{scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{type}({scale})",
        _ => type
    };

    private static Dictionary<string, ColumnInfo> ToMap(IEnumerable<ColumnInfo> columns)
    {
        var map = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
            map.TryAdd(column.Name, column);
        return map;
    }
}
