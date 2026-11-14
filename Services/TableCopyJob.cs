using Microsoft.Data;
using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models;

namespace MigrateSQLServerDatabases.Services;

/// <summary>
/// Copies the selected tables' data from the source to the destination inside a single
/// destination transaction, so the whole copy can be committed or rolled back afterwards.
/// One instance per circuit; a copy that was never committed is rolled back when the circuit ends.
/// </summary>
public sealed class TableCopyJob : IAsyncDisposable
{
    private const int NotifyAfterRows = 500;
    private const int BatchSize = 5000;
    private const long UiThrottleMs = 200;

    // temporal_type only exists from SQL Server 2016, hence the dynamic SQL
    private const string TemporalPairsQuery = """
        IF COL_LENGTH('sys.tables', 'temporal_type') IS NOT NULL
            EXEC sp_executesql N'
                SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + ''.'' + QUOTENAME(t.name),
                       QUOTENAME(SCHEMA_NAME(h.schema_id)) + ''.'' + QUOTENAME(h.name)
                FROM sys.tables t
                INNER JOIN sys.tables h ON h.object_id = t.history_table_id
                WHERE t.temporal_type = 2'
        """;

    private readonly List<TableCopyItem> items = [];
    private readonly HashSet<string> copiedTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock stateLock = new();
    private SqlConnection? destination;
    private SqlTransaction? transaction;
    private Task? runTask;
    private volatile bool stopRequested;
    private long lastNotify;

    public IReadOnlyList<TableCopyItem> Items => items;

    /// <summary>Source tables (full names) whose copy was committed during this session.</summary>
    public IReadOnlySet<string> CopiedTables => copiedTables;

    public CopyJobState State { get; private set; } = CopyJobState.Idle;
    public string? Error { get; private set; }
    public string? CurrentTable { get; private set; }
    public bool ClearDestination { get; set; }

    /// <summary>True while a copy runs or its transaction is waiting for commit / rollback.</summary>
    public bool IsLocked => State is CopyJobState.Copying or CopyJobState.Stopping or CopyJobState.AwaitingDecision
        or CopyJobState.Committing or CopyJobState.RollingBack;

    public long TotalRows => items.Sum(i => i.SourceRows);
    public long ProcessedRows => items.Sum(i => i.ProcessedRows);
    public long CopiedRows => items.Sum(i => i.CopiedRows);
    public long SkippedRows => items.Sum(i => i.SkippedRows);

    public int Percent
    {
        get
        {
            if (State == CopyJobState.Idle || items.Count == 0)
                return 0;

            if (items.All(i => i.Status == TableCopyStatus.Copied))
                return 100;

            var total = TotalRows;
            if (total == 0)
                return items.Count(i => i.Status is TableCopyStatus.Copied or TableCopyStatus.Failed) * 100 / items.Count;

            return (int)Math.Min(100, ProcessedRows * 100 / total);
        }
    }

    public event Action? Changed;

    public bool IsSelected(TableInfo table) => IndexOf(table.FullName) >= 0;

    public void SetSelected(IEnumerable<(TableInfo Source, TableInfo Destination)> tables, bool selected)
    {
        if (IsLocked)
            return;

        // Before applying the change, so re-selecting a table that was just copied adds it fresh
        ResetResults();

        foreach (var (table, destinationTable) in tables)
        {
            var index = IndexOf(table.FullName);
            if (selected && index < 0)
                items.Add(new TableCopyItem(table, destinationTable));
            else if (!selected && index >= 0)
                items.RemoveAt(index);
        }

        items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));
        Notify(force: true);
    }

    /// <summary>Refreshes row counts after the table lists are reloaded and drops tables that no longer match.</summary>
    /// <param name="matches">Destination table for each source table, from <see cref="TableMatcher"/>.</param>
    public void SyncCounts(IReadOnlyList<TableInfo> sourceTables, IReadOnlyDictionary<string, TableInfo> matches)
    {
        if (IsLocked)
            return;

        var source = ToMap(sourceTables);

        items.RemoveAll(i => !source.ContainsKey(i.FullName) || !matches.ContainsKey(i.FullName));
        foreach (var item in items)
        {
            item.Source = source[item.FullName];
            item.Destination = matches[item.FullName];
        }

        Notify(force: true);
    }

    public void Start(string sourceConnectionString, string destinationConnectionString)
    {
        if (IsLocked || items.Count == 0)
            return;

        foreach (var item in items)
            item.Reset();

        Error = null;
        stopRequested = false;
        State = CopyJobState.Copying;
        Notify(force: true);

        runTask = Task.Run(() => RunAsync(sourceConnectionString, destinationConnectionString));
    }

    public void Stop()
    {
        lock (stateLock)
        {
            if (State != CopyJobState.Copying)
                return;

            stopRequested = true;
            State = CopyJobState.Stopping;
        }

        Notify(force: true);
    }

    public async Task CommitAsync()
    {
        if (State != CopyJobState.AwaitingDecision || transaction is null)
            return;

        State = CopyJobState.Committing;
        Notify(force: true);

        try
        {
            await transaction.CommitAsync();
            State = CopyJobState.Committed;

            foreach (var item in items.Where(i => i.Status == TableCopyStatus.Copied))
                copiedTables.Add(item.FullName);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            MarkRolledBack();
            State = CopyJobState.Failed;
        }
        finally
        {
            await CloseAsync();
            Notify(force: true);
        }
    }

    public async Task RollbackAsync()
    {
        if (State != CopyJobState.AwaitingDecision)
            return;

        State = CopyJobState.RollingBack;
        Notify(force: true);

        await RollbackTransactionAsync();
        MarkRolledBack();
        State = CopyJobState.RolledBack;
        Notify(force: true);
    }

    public async ValueTask DisposeAsync()
    {
        stopRequested = true;
        if (runTask is not null)
            await runTask;

        await RollbackTransactionAsync();
    }

    private async Task RunAsync(string sourceConnectionString, string destinationConnectionString)
    {
        try
        {
            destination = new SqlConnection(SqlServerService.Normalize(destinationConnectionString));
            await destination.OpenAsync();
            transaction = (SqlTransaction)await destination.BeginTransactionAsync();

            await using var source = new SqlConnection(SqlServerService.Normalize(sourceConnectionString));
            await source.OpenAsync();

            // History tables only accept direct inserts/deletes while system versioning is off.
            // This happens inside the transaction, so a rollback restores versioning too.
            var temporalPairs = await GetTemporalPairsAsync();
            foreach (var (current, _) in temporalPairs)
                await ExecuteAsync($"ALTER TABLE {current} SET (SYSTEM_VERSIONING = OFF)");

            if (ClearDestination)
                await ClearDestinationAsync();

            var hasGeneratedAlways = await ScalarAsync("SELECT COL_LENGTH('sys.columns', 'generated_always_type')") is not DBNull;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Status == TableCopyStatus.Failed)
                    continue;

                if (stopRequested)
                {
                    item.Status = TableCopyStatus.Stopped;
                    continue;
                }

                await CopyTableAsync(source, item, $"t{i}", hasGeneratedAlways);
            }

            foreach (var (current, history) in temporalPairs)
                await ExecuteAsync($"ALTER TABLE {current} SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = {history}, DATA_CONSISTENCY_CHECK = OFF))");

            lock (stateLock)
                State = CopyJobState.AwaitingDecision;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            await RollbackTransactionAsync();
            MarkRolledBack();

            lock (stateLock)
                State = CopyJobState.Failed;
        }
        finally
        {
            CurrentTable = null;
            Notify(force: true);
        }
    }

    private async Task ClearDestinationAsync()
    {
        var pending = items.ToList();
        pending.ForEach(i => i.Status = TableCopyStatus.Clearing);
        Notify(force: true);

        // Deleting a parent before its children fails with FK error 547 (statement-level, the
        // transaction survives), so keep retrying the blocked tables until a pass makes no progress.
        while (pending.Count > 0)
        {
            var blocked = new List<TableCopyItem>();
            foreach (var item in pending)
            {
                CurrentTable = item.FullName;
                Notify();
                try
                {
                    await ExecuteAsync($"DELETE FROM {item.DestinationName}");
                    item.Status = TableCopyStatus.Pending;
                    item.Error = null;
                }
                catch (SqlException ex) when (ex.Number == 547)
                {
                    item.Error = ex.Message;
                    blocked.Add(item);
                }
            }

            if (blocked.Count == pending.Count)
            {
                blocked.ForEach(i => i.Status = TableCopyStatus.Failed);
                break;
            }

            pending = blocked;
        }
    }

    private async Task CopyTableAsync(SqlConnection source, TableCopyItem item, string savepoint, bool hasGeneratedAlways)
    {
        CurrentTable = item.FullName;
        item.Status = TableCopyStatus.Copying;
        Notify(force: true);
        transaction!.Save(savepoint);

        try
        {
            var before = await CountRowsAsync(item.DestinationName);
            var columns = await GetColumnMappingsAsync(source, item, hasGeneratedAlways);
            if (columns.Count == 0)
                throw new InvalidOperationException("No matching columns between the source and destination table.");

            long processed;
            if (before == 0)
            {
                // Nothing to compare against: copy straight into the table (fastest)
                await BulkCopyAsync(source, item, columns, item.DestinationName);
                processed = await CountRowsAsync(item.DestinationName);
            }
            else
            {
                processed = await CopyMissingRowsAsync(source, item, columns);
            }

            item.CopiedRows = await CountRowsAsync(item.DestinationName) - before;
            item.ProcessedRows = processed;
            item.SkippedRows = Math.Max(0, processed - item.CopiedRows);
            item.Status = TableCopyStatus.Copied;
        }
        catch (Exception ex) when (transaction.Connection is not null)
        {
            // Undo only this table; tables copied before it stay in the transaction
            transaction.Rollback(savepoint);
            item.CopiedRows = 0;
            item.ProcessedRows = 0;
            item.SkippedRows = 0;

            // A failed bulk copy leaves IDENTITY_INSERT on for this table, which would make the
            // next identity table fail ("IDENTITY_INSERT is already ON"); savepoints don't undo SET options
            await ExecuteAsync($"IF OBJECTPROPERTY(OBJECT_ID(N'{item.DestinationName.Replace("'", "''")}'), 'TableHasIdentity') = 1 SET IDENTITY_INSERT {item.DestinationName} OFF");

            if (stopRequested && ex is OperationAbortedException)
            {
                item.Status = TableCopyStatus.Stopped;
            }
            else
            {
                item.Status = TableCopyStatus.Failed;
                item.Error = ex.Message;
            }
        }
    }

    /// <summary>
    /// The destination already has rows: stage the source rows in a temp table on the destination
    /// server, then insert only the rows whose key isn't there yet, in one set-based statement.
    /// </summary>
    /// <returns>Number of source rows that were compared.</returns>
    private async Task<long> CopyMissingRowsAsync(SqlConnection source, TableCopyItem item, List<ColumnMapping> columns)
    {
        const string Stage = "#copy_stage";
        var target = item.DestinationName;
        var columnList = string.Join(", ", columns.Select(c => TableInfo.Quote(c.Destination)));

        // The join stops SELECT INTO from copying the IDENTITY property; column types and collations are kept
        await ExecuteAsync($"""
            IF OBJECT_ID('tempdb..{Stage}') IS NOT NULL DROP TABLE {Stage};
            SELECT TOP (0) {string.Join(", ", columns.Select(c => $"d.{TableInfo.Quote(c.Destination)}"))}
            INTO {Stage}
            FROM {target} d LEFT JOIN (SELECT 1 AS x) AS nothing ON 1 = 0;
            """);

        await BulkCopyAsync(source, item, columns, Stage);
        var staged = (long)(await ScalarAsync($"SELECT COUNT_BIG(*) FROM {Stage}"))!;

        var key = await GetRowKeyAsync(target, columns);
        var notExists = key.IsPrimaryOrUnique
            ? string.Join(" AND ", key.Columns.Select(c => $"d.{TableInfo.Quote(c)} = s.{TableInfo.Quote(c)}"))
            // No usable key: a row counts as existing when every compared column matches (INTERSECT treats NULLs as equal)
            : $"EXISTS (SELECT {string.Join(", ", key.Columns.Select(c => $"d.{TableInfo.Quote(c)}"))} INTERSECT SELECT {string.Join(", ", key.Columns.Select(c => $"s.{TableInfo.Quote(c)}"))})";

        var insert = key.Columns.Count == 0
            ? $"INSERT INTO {target} ({columnList}) SELECT {columnList} FROM {Stage};"
            : $"INSERT INTO {target} ({columnList}) SELECT {columnList} FROM {Stage} s WHERE NOT EXISTS (SELECT 1 FROM {target} d WHERE {notExists});";

        var hasIdentity = columns.Any(c => c.IsIdentity);
        await ExecuteAsync($"""
            {(hasIdentity ? $"SET IDENTITY_INSERT {target} ON;" : string.Empty)}
            {insert}
            {(hasIdentity ? $"SET IDENTITY_INSERT {target} OFF;" : string.Empty)}
            DROP TABLE {Stage};
            """);

        return staged;
    }

    /// <summary>
    /// Columns that identify a row: the primary key, else the first unfiltered unique index, else (e.g. temporal
    /// history tables) every copied column whose type can be compared. Key columns missing in the source can't be used.
    /// </summary>
    private async Task<(List<string> Columns, bool IsPrimaryOrUnique)> GetRowKeyAsync(string table, List<ColumnMapping> columns)
    {
        const string KeySql = """
            SELECT c.name
            FROM sys.index_columns ic
            INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = OBJECT_ID(@table) AND ic.is_included_column = 0 AND ic.index_id = (
                SELECT TOP (1) i.index_id FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(@table) AND i.is_unique = 1 AND i.has_filter = 0
                ORDER BY i.is_primary_key DESC, i.index_id)
            ORDER BY ic.key_ordinal
            """;

        var keyColumns = (await ReadColumnsAsync(new SqlCommand(KeySql, destination, transaction), table)).Select(c => c.Name).ToList();
        var copied = columns.Select(c => c.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (keyColumns.Count > 0 && keyColumns.All(copied.Contains))
            return (keyColumns, true);

        string[] notComparable = ["xml", "text", "ntext", "image", "geography", "geometry"];
        return (columns.Where(c => !notComparable.Contains(c.Type, StringComparer.OrdinalIgnoreCase)).Select(c => c.Destination).ToList(), false);
    }

    private async Task BulkCopyAsync(SqlConnection source, TableCopyItem item, List<ColumnMapping> columns, string destinationTable)
    {
        var select = $"SELECT {string.Join(", ", columns.Select(c => $"{c.Expression} AS {TableInfo.Quote(c.Source)}"))} FROM {item.FullName}";
        await using var command = new SqlCommand(select, source) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync();

        try
        {
            using var bulkCopy = new SqlBulkCopy(
                destination!,
                SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock,
                transaction)
            {
                DestinationTableName = destinationTable,
                BatchSize = BatchSize,
                BulkCopyTimeout = 0,
                NotifyAfter = NotifyAfterRows,
                EnableStreaming = true
            };

            foreach (var column in columns)
                bulkCopy.ColumnMappings.Add(column.Source, column.Destination);

            bulkCopy.SqlRowsCopied += (_, e) =>
            {
                item.ProcessedRows = e.RowsCopied;
                if (stopRequested)
                    e.Abort = true;
                Notify();
            };

            await bulkCopy.WriteToServerAsync(reader);
        }
        catch
        {
            // Otherwise disposing the reader would read the rest of the source table
            command.Cancel();
            throw;
        }
    }

    /// <summary>
    /// Columns present in both tables, skipping destination columns that cannot be inserted:
    /// computed, rowversion and temporal period (GENERATED ALWAYS) columns.
    /// </summary>
    private async Task<List<ColumnMapping>> GetColumnMappingsAsync(
        SqlConnection source, TableCopyItem item, bool hasGeneratedAlways)
    {
        const string ColumnsSql = "SELECT c.name, c.collation_name, c.is_identity, TYPE_NAME(c.system_type_id) FROM sys.columns c WHERE c.object_id = OBJECT_ID(@table)";
        var destinationSql = ColumnsSql + " AND c.is_computed = 0 AND c.system_type_id <> 189"
            + (hasGeneratedAlways ? " AND c.generated_always_type = 0" : string.Empty)
            + " ORDER BY c.column_id";

        var destinationColumns = await ReadColumnsAsync(new SqlCommand(destinationSql, destination, transaction), item.DestinationName);
        var sourceColumns = await ReadColumnsAsync(new SqlCommand(ColumnsSql, source), item.FullName);

        var sourceByName = new Dictionary<string, DbColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in sourceColumns)
            sourceByName.TryAdd(column.Name, column);

        var mappings = new List<ColumnMapping>();
        foreach (var target in destinationColumns)
        {
            if (!sourceByName.TryGetValue(target.Name, out var column))
                continue;

            var expression = TableInfo.Quote(column.Name);

            // SqlBulkCopy rejects text columns whose collation locale differs ("The locale id '1025' of the
            // source column ... and the locale id '1033' of the destination column ... do not match"), even
            // nvarchar to nvarchar. Converting on the source side fixes it; going through nvarchar keeps
            // every character the destination column can hold.
            if (column.Collation is not null && target.Collation is not null &&
                !string.Equals(column.Collation, target.Collation, StringComparison.OrdinalIgnoreCase) &&
                target.Collation.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            {
                expression = $"CAST({expression} AS nvarchar(max)) COLLATE {target.Collation}";
            }

            mappings.Add(new ColumnMapping(expression, column.Name, target.Name, target.IsIdentity, target.Type));
        }

        return mappings;
    }

    private async Task<List<(string Current, string History)>> GetTemporalPairsAsync()
    {
        var selected = items.Select(i => i.DestinationName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairs = new List<(string, string)>();

        await using var command = new SqlCommand(TemporalPairsQuery, destination, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var current = reader.GetString(0);
            var history = reader.GetString(1);
            if (selected.Contains(current) || selected.Contains(history))
                pairs.Add((current, history));
        }

        return pairs;
    }

    /// <summary>Reads column rows; only the first column (name) is required, the others are read when selected.</summary>
    private static async Task<List<DbColumn>> ReadColumnsAsync(SqlCommand command, string table)
    {
        await using (command)
        {
            command.Parameters.AddWithValue("@table", table);
            await using var reader = await command.ExecuteReaderAsync();

            var columns = new List<DbColumn>();
            while (await reader.ReadAsync())
            {
                columns.Add(new DbColumn(
                    reader.GetString(0),
                    reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetString(1) : null,
                    reader.FieldCount > 2 && reader.GetBoolean(2),
                    reader.FieldCount > 3 ? reader.GetString(3) : string.Empty));
            }

            return columns;
        }
    }

    private sealed record DbColumn(string Name, string? Collation, bool IsIdentity, string Type);

    /// <param name="Expression">What the source SELECT reads, e.g. the quoted column or a collation conversion.</param>
    /// <param name="Source">Column name in the source reader (the expression's alias).</param>
    /// <param name="Type">Destination base type name.</param>
    private sealed record ColumnMapping(string Expression, string Source, string Destination, bool IsIdentity, string Type);

    private async Task<long> CountRowsAsync(string table) =>
        (long)(await ScalarAsync($"SELECT COUNT_BIG(*) FROM {table}"))!;

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var command = new SqlCommand(sql, destination, transaction) { CommandTimeout = 0 };
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new SqlCommand(sql, destination, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    private async Task RollbackTransactionAsync()
    {
        try
        {
            if (transaction?.Connection is not null)
                await transaction.RollbackAsync();
        }
        catch (Exception)
        {
            // Closing the connection below rolls back a pending transaction anyway
        }
        finally
        {
            await CloseAsync();
        }
    }

    private async Task CloseAsync()
    {
        transaction?.Dispose();
        transaction = null;

        if (destination is not null)
            await destination.DisposeAsync();
        destination = null;
    }

    private void MarkRolledBack()
    {
        foreach (var item in items.Where(i => i.Status == TableCopyStatus.Copied))
            item.Status = TableCopyStatus.RolledBack;
    }

    private void ResetResults()
    {
        if (State is not (CopyJobState.Committed or CopyJobState.RolledBack or CopyJobState.Failed))
            return;

        // Saved tables are hidden from the source list, so drop them from the selection too;
        // otherwise the next copy would copy them again
        if (State == CopyJobState.Committed)
            items.RemoveAll(i => i.Status == TableCopyStatus.Copied);

        State = CopyJobState.Idle;
        Error = null;
        foreach (var item in items)
            item.Reset();
    }

    private int IndexOf(string fullName) =>
        items.FindIndex(i => string.Equals(i.FullName, fullName, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, TableInfo> ToMap(IEnumerable<TableInfo> tables)
    {
        var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
            map.TryAdd(table.FullName, table);
        return map;
    }

    private void Notify(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref lastNotify) < UiThrottleMs)
            return;

        Interlocked.Exchange(ref lastNotify, now);
        Changed?.Invoke();
    }
}
