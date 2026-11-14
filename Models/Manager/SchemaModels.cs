using MigrateSQLServerDatabases.Services.Manager;

namespace MigrateSQLServerDatabases.Models.Manager;

public sealed class TableDefinition
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public int ObjectId { get; init; }
    public string? DatabaseCollation { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }
    public long RowCount { get; set; }
    public long? ReservedKb { get; set; }

    public List<ColumnDefinition> Columns { get; } = [];

    /// <summary>All indexes, including the ones behind PRIMARY KEY and UNIQUE constraints.</summary>
    public List<IndexDefinition> Indexes { get; } = [];
    public List<ForeignKeyDefinition> ForeignKeys { get; } = [];
    public List<CheckDefinition> Checks { get; } = [];

    public IndexDefinition? PrimaryKey => Indexes.FirstOrDefault(i => i.IsPrimaryKey);

    public string FullName => Sql.Q(Schema, Name);
}

public sealed class ColumnDefinition
{
    public int ColumnId { get; init; }
    public required string Name { get; init; }

    /// <summary>Type name as declared (nvarchar, int, or an alias type such as sysname).</summary>
    public required string DataType { get; init; }

    /// <summary>Schema of a user-defined type; null for system types.</summary>
    public string? TypeSchema { get; init; }

    /// <summary>System type behind the column, e.g. nvarchar for sysname.</summary>
    public string BaseType { get; init; } = string.Empty;

    /// <summary>"50", "max", "18,2", "7"; null when the type takes no length.</summary>
    public string? Length { get; init; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }
    public string Seed { get; init; } = "1";
    public string Increment { get; init; } = "1";
    public bool IsComputed { get; init; }
    public string? ComputedDefinition { get; init; }
    public bool IsPersisted { get; init; }
    public string? DefaultName { get; init; }
    public string? DefaultDefinition { get; init; }
    public string? Collation { get; init; }
    public bool IsRowGuid { get; init; }
    public bool IsSparse { get; init; }

    public bool IsRowVersion => BaseType is "timestamp";

    public string TypeSql => ScriptBuilder.TypeSql(DataType, TypeSchema, Length);
}

public sealed record IndexColumn(string Name, bool Descending);

public sealed class IndexDefinition
{
    public int IndexId { get; init; }
    public required string Name { get; init; }

    /// <summary>1 clustered, 2 nonclustered, 3 XML, 4 spatial, 5 clustered columnstore, 6 nonclustered columnstore, 7 hash.</summary>
    public int Type { get; init; }
    public string TypeDesc { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsUniqueConstraint { get; init; }
    public bool IsDisabled { get; init; }
    public int FillFactor { get; init; }
    public string? Filter { get; init; }
    public List<IndexColumn> Columns { get; } = [];
    public List<string> Included { get; } = [];

    public bool IsConstraint => IsPrimaryKey || IsUniqueConstraint;
}

public sealed class ForeignKeyDefinition
{
    public int ObjectId { get; init; }
    public required string Name { get; init; }
    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public List<string> Columns { get; } = [];
    public List<string> ReferencedColumns { get; } = [];

    /// <summary>NO_ACTION, CASCADE, SET_NULL or SET_DEFAULT.</summary>
    public string DeleteAction { get; init; } = "NO_ACTION";
    public string UpdateAction { get; init; } = "NO_ACTION";
    public bool IsDisabled { get; init; }
    public bool IsNotTrusted { get; init; }
}

public sealed record CheckDefinition(string Name, string Definition, bool IsDisabled);

public sealed record ParameterDefinition(int Id, string Name, string TypeSql, string BaseType, bool IsOutput, bool HasDefault);

/// <summary>A column in the table designer; <see cref="Original"/> is null for a new column.</summary>
public sealed class DesignColumn
{
    public ColumnDefinition? Original { get; init; }
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "nvarchar";
    public string? Length { get; set; } = "50";
    public bool AllowNulls { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string Seed { get; set; } = "1";
    public string Increment { get; set; } = "1";
    public string? Default { get; set; }
    public bool Removed { get; set; }

    public bool IsNew => Original is null;

    /// <summary>Computed columns are shown but not edited by the designer.</summary>
    public bool IsComputed => Original?.IsComputed == true;

    public string TypeSql => ScriptBuilder.TypeSql(DataType, Original?.TypeSchema, Length);

    public static DesignColumn From(ColumnDefinition column, bool isPrimaryKey) => new()
    {
        Original = column,
        Name = column.Name,
        DataType = column.DataType,
        Length = column.Length,
        AllowNulls = column.IsNullable,
        IsPrimaryKey = isPrimaryKey,
        IsIdentity = column.IsIdentity,
        Seed = column.Seed,
        Increment = column.Increment,
        Default = column.DefaultDefinition
    };
}

public enum EditRowState
{
    Unchanged,
    Modified,
    Added,
    Deleted
}

public sealed class EditColumn
{
    public required string Name { get; init; }

    /// <summary>System type name, lower case.</summary>
    public required string Type { get; init; }
    public required string TypeDisplay { get; init; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsComputed { get; init; }
    public bool IsKey { get; set; }

    /// <summary>CLR types (geography, geometry, hierarchyid) are read and written as text.</summary>
    public bool IsClrType { get; init; }

    public bool IsReadOnly => IsIdentity || IsComputed || Type == "timestamp";

    /// <summary>Can be used in a WHERE ... = @value to find the row again.</summary>
    public bool IsComparable => !IsClrType && Type is not ("text" or "ntext" or "image" or "xml" or "sql_variant");

    public bool IsText => Type is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" || IsClrType;
}

public sealed class EditRow
{
    public required object?[] Original { get; init; }
    public required string?[] Values { get; init; }
    public required bool[] Changed { get; init; }
    public EditRowState State { get; set; }
    public string? Error { get; set; }
}

/// <summary>"Edit Top 200 Rows" grid.</summary>
public sealed class EditData
{
    public required List<EditColumn> Columns { get; init; }
    public List<EditRow> Rows { get; } = [];

    /// <summary>Columns used to find a row: the primary key, a unique index, or every comparable column.</summary>
    public List<int> KeyIndexes { get; } = [];

    /// <summary>No primary key or unique index; rows are matched on all comparable columns with TOP (1).</summary>
    public bool MatchesAllColumns { get; set; }
    public string SelectSql { get; set; } = string.Empty;

    public bool CanEdit => KeyIndexes.Count > 0;

    public int PendingChanges => Rows.Count(r => r.State != EditRowState.Unchanged);
}

public sealed record DatabaseFile(
    int FileId, string Name, string Type, string? FileGroup, string PhysicalName,
    decimal SizeMb, decimal? UsedMb, int Growth, bool IsPercentGrowth, long MaxSize)
{
    public string GrowthDisplay => Growth == 0 ? "—" : IsPercentGrowth ? $"{Growth}%" : $"{Growth * 8 / 1024m:0.##} MB";

    /// <summary>-1 and 268435456 pages (2 TB) both mean unlimited.</summary>
    public bool IsUnlimited => MaxSize is -1 or 268435456;

    public string MaxSizeDisplay => IsUnlimited ? "∞" : $"{MaxSize * 8 / 1024m:0.##} MB";
}

public sealed class DatabaseProperties
{
    public required string Name { get; init; }
    public string State { get; init; } = string.Empty;
    public string UserAccess { get; set; } = "MULTI_USER";
    public bool IsReadOnly { get; set; }
    public string RecoveryModel { get; set; } = "FULL";
    public int CompatibilityLevel { get; set; }
    public string? Collation { get; set; }
    public string? Owner { get; set; }
    public DateTime? Created { get; init; }
    public string PageVerify { get; set; } = "CHECKSUM";
    public bool AutoClose { get; set; }
    public bool AutoShrink { get; set; }
    public bool AutoCreateStats { get; set; }
    public bool AutoUpdateStats { get; set; }
    public bool AllowSnapshotIsolation { get; set; }
    public bool ReadCommittedSnapshot { get; set; }
    public decimal SizeMb { get; init; }
    public DateTime? LastFullBackup { get; init; }
    public DateTime? LastLogBackup { get; init; }
    public int Users { get; init; }
    public List<DatabaseFile> Files { get; } = [];

    public DatabaseProperties Clone() => (DatabaseProperties)MemberwiseClone();
}

public sealed record TableSize(string Schema, string Name, long Rows, long ReservedKb, long DataKb, long IndexKb, long UnusedKb);

public sealed record BackupSet(
    int Position, int Type, string? Name, string DatabaseName, string ServerName,
    DateTime? Start, DateTime? Finish, decimal SizeMb, bool IsCopyOnly)
{
    /// <summary>RESTORE HEADERONLY BackupType: 1 full, 2 log, 5 differential.</summary>
    public string TypeKey => Type switch { 1 => "BackupFull", 2 => "BackupLog", 5 => "BackupDifferential", _ => "BackupOther" };
}

/// <param name="Type">D data, L log, F full-text, S filestream.</param>
public sealed record BackupFile(string LogicalName, string PhysicalName, string Type);

public sealed record FileEntry(string Name, bool IsFile);

public sealed record SessionActivity(
    int SessionId, string? Login, string? Host, string? Program, string? Database,
    string? Status, string? Command, string? WaitType, long WaitMs, int BlockedBy,
    long CpuMs, long Reads, long Writes, long LogicalReads, long ElapsedMs, int? PercentComplete,
    DateTime? LoginTime, DateTime? LastRequest, string? SqlText, int OpenTransactions);

public sealed record ExpensiveQuery(
    long Executions, long TotalCpuMs, long AvgCpuMs, long TotalReads, long TotalElapsedMs,
    DateTime? LastRun, string? Database, string? Text);

public sealed record ServerConfiguration(
    int Id, string Name, long Value, long ValueInUse, long Minimum, long Maximum,
    bool IsDynamic, bool IsAdvanced, string? Description);

public sealed record LoginDetails(
    string Name, string Type, bool IsDisabled, string? DefaultDatabase,
    bool CheckPolicy, bool CheckExpiration, List<string> ServerRoles);

/// <summary>The database user a login maps to.</summary>
public sealed class LoginMapping
{
    public required string Database { get; init; }
    public string? OriginalUser { get; init; }
    public bool Mapped { get; set; }
    public string User { get; set; } = string.Empty;
    public string DefaultSchema { get; set; } = "dbo";
    public HashSet<string> OriginalRoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Roles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record UserDetails(string Name, string Type, string? Login, string? DefaultSchema, List<string> Roles);
