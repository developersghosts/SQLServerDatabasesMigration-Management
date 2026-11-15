using System.Collections;
using System.Collections.Concurrent;

namespace MigrateSQLServerDatabases.Models.Manager;

public enum MessageKind
{
    Info,
    Error,
    RowsAffected,
    Success
}

/// <param name="Line">Line in the editor, for errors that can be jumped to.</param>
public sealed record QueryMessage(MessageKind Kind, string Text, int? Line = null)
{
    public static QueryMessage Rows(int count) =>
        new(MessageKind.RowsAffected, count == 1 ? "(1 row affected)" : $"({count} rows affected)");
}

public sealed record ResultColumn(string Name, string TypeName)
{
    public bool IsNumeric => TypeName is "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric"
        or "money" or "smallmoney" or "float" or "real";
}

/// <summary>One grid of results. Values are kept formatted; null is SQL NULL.</summary>
public sealed class ResultSet(List<ResultColumn> columns)
{
    public List<ResultColumn> Columns { get; } = columns;
    public List<string?[]> Rows { get; } = [];

    /// <summary>Rows returned by the server; more than <see cref="Rows"/> when the grid limit was reached.</summary>
    public long TotalRows { get; set; }
    public bool Truncated { get; set; }

    /// <summary>MySQL / PostgreSQL / Oracle: the base table and column of each result column (null when computed).</summary>
    public List<ColumnOrigin?>? Origins { get; set; }

    /// <summary>The batch that returned this grid, its database and the grid's position in it; used to edit cells.</summary>
    public string? BatchSql { get; init; }
    public string? Database { get; init; }
    public int IndexInBatch { get; init; }

    /// <summary>Which cells can be edited and each column's keys, worked out when the grid first shows.</summary>
    public Task<ResultEditInfo>? EditInfo { get; set; }

    /// <summary>Vertical view: one record at a time, its fields listed down the page.</summary>
    public bool Vertical { get; set; }

    /// <summary>Record shown in the vertical view.</summary>
    public int Record { get; set; }

    /// <summary>Row indexes for Virtualize, which needs an ICollection.</summary>
    public RowIndexList Indexes => indexes ??= new RowIndexList(Rows);
    private RowIndexList? indexes;
}

/// <summary>Base table and column a result column was read from, as reported by the provider.</summary>
public sealed record ColumnOrigin(string? Schema, string Table, string Column);

/// <summary>Where an edited cell is saved: its base table and column, and the result columns holding the row's key.</summary>
/// <param name="Table">[schema].[table] in <paramref name="Database"/>.</param>
public sealed record CellEditTarget(
    string Database,
    string Table,
    string Column,
    string Type,
    bool IsText,
    bool IsNullable,
    IReadOnlyList<(int Index, string Column, string Type)> Keys);

/// <summary>Where a result column comes from and its keys, shown on the column header.</summary>
/// <param name="ForeignKey">Referenced table and column, when the column is a foreign key.</param>
public sealed record ResultColumnInfo(
    string Database,
    string Schema,
    string Table,
    string Column,
    bool IsPrimaryKey,
    bool IsUnique,
    (string Schema, string Table, string Column)? ForeignKey,
    bool IsIdentity,
    bool IsComputed,
    bool IsNullable,
    string Type)
{
    public string ForeignKeyDisplay => ForeignKey is { } fk ? $"{fk.Schema}.{fk.Table}.{fk.Column}" : string.Empty;
}

/// <param name="Reason">Translation key explaining why nothing can be edited, when <see cref="Columns"/> is empty.</param>
public sealed record ResultEditInfo(IReadOnlyDictionary<int, CellEditTarget> Columns, string? Reason, string? Detail = null)
{
    /// <summary>Source and keys per result column (known even when nothing can be edited).</summary>
    public IReadOnlyDictionary<int, ResultColumnInfo> Info { get; init; } = new Dictionary<int, ResultColumnInfo>();

    public static ResultEditInfo No(string reason, string? detail = null) => new(new Dictionary<int, CellEditTarget>(), reason, detail);
}

/// <summary>Read-only list 0..Count-1 over a growing list, so rows can be virtualized while they stream in.</summary>
public sealed class RowIndexList(IList source) : IList<int>
{
    public int this[int index] { get => index; set => throw new NotSupportedException(); }
    public int Count => source.Count;
    public bool IsReadOnly => true;
    public int IndexOf(int item) => item >= 0 && item < Count ? item : -1;
    public bool Contains(int item) => item >= 0 && item < Count;

    public void CopyTo(int[] array, int arrayIndex)
    {
        for (var i = 0; i < Count; i++)
            array[arrayIndex + i] = i;
    }

    public IEnumerator<int> GetEnumerator()
    {
        var count = Count;
        for (var i = 0; i < count; i++)
            yield return i;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Add(int item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, int item) => throw new NotSupportedException();
    public bool Remove(int item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}

/// <summary>
/// Results and messages of one run. SqlClient raises info messages on I/O threads, so they are queued
/// and moved into <see cref="Messages"/> on the render thread by <see cref="DrainPending"/>.
/// </summary>
public sealed class QueryExecution
{
    private readonly ConcurrentQueue<QueryMessage> pending = new();
    private volatile bool hasErrors;

    public List<ResultSet> Results { get; } = [];
    public List<QueryMessage> Messages { get; } = [];
    public DateTime Started { get; set; } = DateTime.Now;
    public TimeSpan Elapsed { get; set; }
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public bool HasErrors => hasErrors;

    public long RowsReturned => Results.Sum(r => r.TotalRows);

    public void Enqueue(QueryMessage message)
    {
        if (message.Kind == MessageKind.Error)
            hasErrors = true;
        pending.Enqueue(message);
    }

    public void AddError(string text)
    {
        hasErrors = true;
        DrainPending();
        Messages.Add(new QueryMessage(MessageKind.Error, text));
    }

    public void DrainPending()
    {
        while (pending.TryDequeue(out var message))
            Messages.Add(message);
    }
}
