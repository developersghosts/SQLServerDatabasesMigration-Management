namespace MigrateSQLServerDatabases.Models;

public enum DatabaseRole
{
    Source,
    Destination
}

public sealed record ServerInfo(
    string ServerName,
    string DatabaseName,
    string ProductVersion,
    string ProductLevel,
    string Edition,
    int EngineEdition)
{
    public int MajorVersion =>
        int.TryParse(ProductVersion.Split('.')[0], out var major) ? major : 0;

    // EngineEdition 5 = Azure SQL Database, 8 = Azure SQL Managed Instance
    public string FriendlyVersion => EngineEdition switch
    {
        5 => "Azure SQL Database",
        8 => "Azure SQL Managed Instance",
        _ => MajorVersion switch
        {
            8 => "SQL Server 2000",
            9 => "SQL Server 2005",
            10 when ProductVersion.StartsWith("10.5") => "SQL Server 2008 R2",
            10 => "SQL Server 2008",
            11 => "SQL Server 2012",
            12 => "SQL Server 2014",
            13 => "SQL Server 2016",
            14 => "SQL Server 2017",
            15 => "SQL Server 2019",
            16 => "SQL Server 2022",
            17 => "SQL Server 2025",
            _ => $"SQL Server (v{ProductVersion})"
        }
    };
}

public sealed record TableInfo(string Schema, string Name, long RowCount)
{
    // Same format as QUOTENAME(schema) + '.' + QUOTENAME(name)
    public string FullName => $"{Quote(Schema)}.{Quote(Name)}";

    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}

public sealed class DatabaseConnection
{
    public string ConnectionString { get; set; } = string.Empty;
    public ServerInfo? Server { get; set; }
    public string? Error { get; set; }

    public bool IsConnected => Server is not null;
}
