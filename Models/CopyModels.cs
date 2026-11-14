namespace MigrateSQLServerDatabases.Models;

public enum CopyJobState
{
    Idle,
    Copying,
    Stopping,
    AwaitingDecision,
    Committing,
    RollingBack,
    Committed,
    RolledBack,
    Failed
}

public enum TableCopyStatus
{
    Pending,
    Clearing,
    Copying,
    Copied,
    Stopped,
    Failed,
    RolledBack
}

/// <param name="destination">The matched destination table; its name can differ from the source (Customers → Customer).</param>
public sealed class TableCopyItem(TableInfo source, TableInfo destination)
{
    public TableInfo Source { get; set; } = source;
    public TableInfo Destination { get; set; } = destination;

    /// <summary>Source full name; identifies the selection.</summary>
    public string FullName => Source.FullName;
    public string DestinationName => Destination.FullName;
    public bool IsRenamed => !string.Equals(FullName, DestinationName, StringComparison.OrdinalIgnoreCase);

    public long SourceRows => Source.RowCount;
    public long DestinationRows => Destination.RowCount;

    /// <summary>Source rows read so far; drives the progress bar.</summary>
    public long ProcessedRows { get; set; }

    /// <summary>Rows actually inserted, verified against the destination when the table finishes.</summary>
    public long CopiedRows { get; set; }

    /// <summary>Source rows not inserted because the destination already had a row with the same key.</summary>
    public long SkippedRows { get; set; }

    public TableCopyStatus Status { get; set; }
    public string? Error { get; set; }

    public void Reset()
    {
        ProcessedRows = 0;
        CopiedRows = 0;
        SkippedRows = 0;
        Status = TableCopyStatus.Pending;
        Error = null;
    }
}
