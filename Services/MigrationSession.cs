using MigrateSQLServerDatabases.Models;

namespace MigrateSQLServerDatabases.Services;

/// <summary>
/// Holds the source and destination connections for the current user's circuit.
/// Kept in memory only; nothing is written to disk.
/// </summary>
public sealed class MigrationSession
{
    public DatabaseConnection Source { get; } = new();
    public DatabaseConnection Destination { get; } = new();

    // Last loaded table lists; reused while a copy transaction holds locks in the destination
    public List<TableInfo>? SourceTables { get; set; }
    public List<TableInfo>? DestinationTables { get; set; }

    public bool IsReady => Source.IsConnected && Destination.IsConnected;

    public bool IsSameDatabase =>
        Source.Server is { } s && Destination.Server is { } d &&
        string.Equals(s.ServerName, d.ServerName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(s.DatabaseName, d.DatabaseName, StringComparison.OrdinalIgnoreCase);

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
