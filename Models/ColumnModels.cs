namespace MigrateSQLServerDatabases.Models;

/// <param name="IsAutoFilled">SQL Server fills it on insert: identity, computed, rowversion, default constraint or GENERATED ALWAYS.</param>
/// <param name="Collation">Only set for text columns.</param>
/// <param name="CodePage">Code page of the collation; 0 for non-text columns, 65001 for UTF-8 collations.</param>
public sealed record ColumnInfo(
    string Name,
    string Type,
    bool IsNullable,
    bool IsAutoFilled,
    string? Collation,
    int CodePage,
    bool IsUnicode)
{
    public bool IsText => Collation is not null;

    public string TypeDisplay => $"{Type} {(IsNullable ? "NULL" : "NOT NULL")}";

    public string Display => IsText ? $"{TypeDisplay} · {Collation}" : TypeDisplay;
}

public enum ColumnDiffKind
{
    DestinationOnly,
    SourceOnly,
    TypeMismatch,
    CollationMismatch
}

public sealed record ColumnDiff(string Column, ColumnDiffKind Kind, ColumnInfo? Source, ColumnInfo? Destination)
{
    /// <summary>A new destination column that can't be left empty, so inserting into the table fails.</summary>
    public bool IsBlocking =>
        Kind == ColumnDiffKind.DestinationOnly && Destination is { IsNullable: false, IsAutoFilled: false };

    /// <summary>
    /// The destination is a non-Unicode column whose code page can't hold every source character
    /// (e.g. Arabic into a Latin varchar); those characters are stored as '?'.
    /// </summary>
    public bool MayLoseCharacters =>
        Source is { IsText: true } source &&
        Destination is { IsText: true, IsUnicode: false } destination &&
        destination.CodePage != 65001 &&
        (source.IsUnicode || source.CodePage != destination.CodePage);
}

public sealed record TableColumnDiff(string Table, string DestinationTable, IReadOnlyList<ColumnDiff> Columns)
{
    public bool IsRenamed => !string.Equals(Table, DestinationTable, StringComparison.OrdinalIgnoreCase);

    public bool IsBlocking => Columns.Any(c => c.IsBlocking);
}

/// <param name="Tables">Tables with differences worth reviewing one by one.</param>
/// <param name="CollationChanges">Text columns that only differ in collation and convert without loss.</param>
/// <param name="CollationPairs">Distinct "source → destination" collations of those columns.</param>
public sealed record ColumnComparison(
    IReadOnlyList<TableColumnDiff> Tables,
    int CollationChanges,
    IReadOnlyList<string> CollationPairs)
{
    public bool HasDifferences => Tables.Count > 0 || CollationChanges > 0;
}
