using System.Text;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>Builds T-SQL from metadata. No database access.</summary>
public static class ScriptBuilder
{
    public const string Go = "GO";

    /// <summary>Types that take a length, precision or scale, with the default used by the designer.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultLengths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["char"] = "10", ["varchar"] = "50", ["nchar"] = "10", ["nvarchar"] = "50",
        ["binary"] = "50", ["varbinary"] = "50", ["decimal"] = "18,2", ["numeric"] = "18,2",
        ["datetime2"] = "7", ["time"] = "7", ["datetimeoffset"] = "7"
    };

    public static readonly string[] SystemTypes =
    [
        "bigint", "binary", "bit", "char", "date", "datetime", "datetime2", "datetimeoffset", "decimal", "float",
        "geography", "geometry", "hierarchyid", "image", "int", "money", "nchar", "ntext", "numeric", "nvarchar",
        "real", "rowversion", "smalldatetime", "smallint", "smallmoney", "sql_variant", "text", "time", "tinyint",
        "uniqueidentifier", "varbinary", "varchar", "xml"
    ];

    public static bool TakesLength(string type) => DefaultLengths.ContainsKey(type);

    public static bool IsTextType(string type) =>
        type.ToLowerInvariant() is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

    /// <summary>nvarchar(50), decimal(18,2), [dbo].[MyType].</summary>
    public static string TypeSql(string dataType, string? typeSchema, string? length)
    {
        if (typeSchema is not null && !typeSchema.Equals("sys", StringComparison.OrdinalIgnoreCase))
            return Sql.Q(typeSchema, dataType);

        return string.IsNullOrWhiteSpace(length) || !TakesLength(dataType)
            ? dataType
            : $"{dataType}({length.Trim()})";
    }

    /// <summary>Length text from sys.columns; null for types that take none (and for alias types).</summary>
    public static string? FormatLength(string type, bool isAlias, int maxLength, int precision, int scale)
    {
        if (isAlias)
            return null;

        return type.ToLowerInvariant() switch
        {
            "nvarchar" or "nchar" => maxLength == -1 ? "max" : (maxLength / 2).ToString(),
            "varchar" or "char" or "varbinary" or "binary" => maxLength == -1 ? "max" : maxLength.ToString(),
            "decimal" or "numeric" => $"{precision},{scale}",
            "datetime2" or "time" or "datetimeoffset" => scale.ToString(),
            _ => null
        };
    }

    public static string FormatType(string type, bool isAlias, string? typeSchema, int maxLength, int precision, int scale) =>
        TypeSql(type, isAlias ? typeSchema : null, FormatLength(type, isAlias, maxLength, precision, scale));

    private static string ColumnSql(ColumnDefinition c, string? databaseCollation)
    {
        if (c.IsComputed)
            return $"{Sql.Q(c.Name)} AS {c.ComputedDefinition}{(c.IsPersisted ? " PERSISTED" : "")}{(c.IsPersisted && !c.IsNullable ? " NOT NULL" : "")}";

        var sb = new StringBuilder();
        sb.Append(Sql.Q(c.Name)).Append(' ').Append(c.TypeSql);
        if (c.Collation is not null && c.TypeSchema is null &&
            !string.Equals(c.Collation, databaseCollation, StringComparison.OrdinalIgnoreCase))
            sb.Append(" COLLATE ").Append(c.Collation);
        if (c.IsSparse)
            sb.Append(" SPARSE");
        if (c.IsIdentity)
            sb.Append($" IDENTITY({c.Seed},{c.Increment})");
        if (c.IsRowGuid)
            sb.Append(" ROWGUIDCOL");
        sb.Append(c.IsNullable ? " NULL" : " NOT NULL");
        return sb.ToString();
    }

    private static string IndexColumns(IEnumerable<IndexColumn> columns) =>
        string.Join(", ", columns.Select(c => $"{Sql.Q(c.Name)} {(c.Descending ? "DESC" : "ASC")}"));

    private static string Clustering(IndexDefinition index) => index.Type == 1 ? "CLUSTERED" : "NONCLUSTERED";

    private static string WithFillFactor(IndexDefinition index) =>
        index.FillFactor is > 0 and < 100 ? $" WITH (FILLFACTOR = {index.FillFactor})" : string.Empty;

    public static string KeyConstraint(IndexDefinition index) =>
        $"CONSTRAINT {Sql.Q(index.Name)} {(index.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE")} {Clustering(index)} ({IndexColumns(index.Columns)}){WithFillFactor(index)}";

    public sealed record TableScriptOptions(bool Indexes = true, bool ForeignKeys = true, bool Checks = true, bool Defaults = true);

    public static string CreateTable(TableDefinition t, TableScriptOptions? options = null)
    {
        options ??= new TableScriptOptions();
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(t.FullName).Append("(\n");

        var lines = t.Columns.Select(c => "    " + ColumnSql(c, t.DatabaseCollation)).ToList();
        lines.AddRange(t.Indexes.Where(i => i.IsConstraint).Select(i => "    " + KeyConstraint(i)));
        sb.Append(string.Join(",\n", lines)).Append("\n);\n").Append(Go).Append('\n');

        if (options.Defaults)
            foreach (var c in t.Columns.Where(c => c.DefaultName is not null))
                sb.Append(AddDefault(t.FullName, c.DefaultName!, c.DefaultDefinition!, c.Name)).Append('\n').Append(Go).Append('\n');

        if (options.Checks)
            foreach (var check in t.Checks)
                sb.Append(AddCheck(t.FullName, check)).Append('\n').Append(Go).Append('\n');

        if (options.ForeignKeys)
            foreach (var fk in t.ForeignKeys)
                sb.Append(AddForeignKey(t.FullName, fk)).Append('\n').Append(Go).Append('\n');

        if (options.Indexes)
            foreach (var index in t.Indexes.Where(i => !i.IsConstraint))
                sb.Append(CreateIndex(t.FullName, index)).Append('\n').Append(Go).Append('\n');

        return sb.ToString();
    }

    public static string AddDefault(string table, string name, string definition, string column) =>
        $"ALTER TABLE {table} ADD CONSTRAINT {Sql.Q(name)} DEFAULT {Wrap(definition)} FOR {Sql.Q(column)};";

    public static string AddCheck(string table, CheckDefinition check)
    {
        var sql = $"ALTER TABLE {table} WITH CHECK ADD CONSTRAINT {Sql.Q(check.Name)} CHECK {Wrap(check.Definition)};";
        return check.IsDisabled ? sql + $"\nALTER TABLE {table} NOCHECK CONSTRAINT {Sql.Q(check.Name)};" : sql;
    }

    public static string AddForeignKey(string table, ForeignKeyDefinition fk)
    {
        var sql = new StringBuilder();
        sql.Append($"ALTER TABLE {table} WITH {(fk.IsNotTrusted ? "NOCHECK" : "CHECK")} ADD CONSTRAINT {Sql.Q(fk.Name)} ");
        sql.Append($"FOREIGN KEY ({string.Join(", ", fk.Columns.Select(Sql.Q))}) ");
        sql.Append($"REFERENCES {Sql.Q(fk.ReferencedSchema, fk.ReferencedTable)} ({string.Join(", ", fk.ReferencedColumns.Select(Sql.Q))})");
        if (fk.DeleteAction != "NO_ACTION")
            sql.Append(" ON DELETE ").Append(fk.DeleteAction.Replace('_', ' '));
        if (fk.UpdateAction != "NO_ACTION")
            sql.Append(" ON UPDATE ").Append(fk.UpdateAction.Replace('_', ' '));
        sql.Append(';');
        if (fk.IsDisabled)
            sql.Append($"\nALTER TABLE {table} NOCHECK CONSTRAINT {Sql.Q(fk.Name)};");
        return sql.ToString();
    }

    public static string CreateIndex(string table, IndexDefinition index)
    {
        if (index.IsConstraint)
            return $"ALTER TABLE {table} ADD {KeyConstraint(index)};";

        var sql = index.Type switch
        {
            1 or 2 => new StringBuilder()
                .Append($"CREATE {(index.IsUnique ? "UNIQUE " : "")}{Clustering(index)} INDEX {Sql.Q(index.Name)} ON {table} ({IndexColumns(index.Columns)})")
                .Append(index.Included.Count > 0 ? $" INCLUDE ({string.Join(", ", index.Included.Select(Sql.Q))})" : "")
                .Append(index.Filter is not null ? $" WHERE {index.Filter}" : "")
                .Append(WithFillFactor(index))
                .Append(';').ToString(),
            5 => $"CREATE CLUSTERED COLUMNSTORE INDEX {Sql.Q(index.Name)} ON {table};",
            6 => $"CREATE NONCLUSTERED COLUMNSTORE INDEX {Sql.Q(index.Name)} ON {table} ({string.Join(", ", index.Columns.Select(c => Sql.Q(c.Name)).Concat(index.Included.Select(Sql.Q)))});",
            _ => $"-- {index.TypeDesc} index {Sql.Q(index.Name)} is not scripted"
        };

        return index.IsDisabled ? sql + $"\nALTER INDEX {Sql.Q(index.Name)} ON {table} DISABLE;" : sql;
    }

    public static string DropIndex(string table, IndexDefinition index) => index.IsConstraint
        ? $"ALTER TABLE {table} DROP CONSTRAINT {Sql.Q(index.Name)};"
        : $"DROP INDEX {Sql.Q(index.Name)} ON {table};";

    /// <summary>Default and check definitions are stored with parentheses; add them when typed without.</summary>
    public static string Wrap(string expression)
    {
        var e = expression.Trim();
        return e.StartsWith('(') && e.EndsWith(')') ? e : $"({e})";
    }

    public static string SelectTop(string database, string table, IEnumerable<string> columns, int top) =>
        $"SELECT TOP ({top})\n       {string.Join("\n      ,", columns.Select(Sql.Q))}\n  FROM {Sql.Q(database)}.{table};";

    public static string InsertTemplate(string table, IReadOnlyList<ColumnDefinition> columns)
    {
        var insertable = columns.Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion).ToList();
        return $"INSERT INTO {table}\n           ({string.Join("\n           ,", insertable.Select(c => Sql.Q(c.Name)))})\n     VALUES\n           ({string.Join("\n           ,", insertable.Select(c => $"<{c.Name}, {c.TypeSql},>"))});";
    }

    public static string UpdateTemplate(string table, IReadOnlyList<ColumnDefinition> columns)
    {
        var updatable = columns.Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion).ToList();
        return $"UPDATE {table}\n   SET {string.Join("\n      ,", updatable.Select(c => $"{Sql.Q(c.Name)} = <{c.Name}, {c.TypeSql},>"))}\n WHERE <Search Conditions,,>;";
    }

    public static string DeleteTemplate(string table) => $"DELETE FROM {table}\n      WHERE <Search Conditions,,>;";

    public static string DropTable(string table) =>
        $"IF OBJECT_ID({Sql.Lit(table)}, N'U') IS NOT NULL\n    DROP TABLE {table};";

    /// <summary>
    /// CREATE TABLE for a new table from the designer.
    /// </summary>
    public static string CreateTableFromDesign(string schema, string name, IReadOnlyList<DesignColumn> columns)
    {
        var table = Sql.Q(schema, name);
        var live = columns.Where(c => !c.Removed && !string.IsNullOrWhiteSpace(c.Name)).ToList();
        var lines = live.Select(c =>
        {
            var line = $"    {Sql.Q(c.Name.Trim())} {c.TypeSql}";
            if (c.IsIdentity)
                line += $" IDENTITY({c.Seed},{c.Increment})";
            line += c.AllowNulls && !c.IsPrimaryKey ? " NULL" : " NOT NULL";
            if (!string.IsNullOrWhiteSpace(c.Default))
                line += $" CONSTRAINT {Sql.Q($"DF_{name}_{c.Name.Trim()}")} DEFAULT {Wrap(c.Default)}";
            return line;
        }).ToList();

        var pk = live.Where(c => c.IsPrimaryKey).ToList();
        if (pk.Count > 0)
            lines.Add($"    CONSTRAINT {Sql.Q($"PK_{name}")} PRIMARY KEY ({string.Join(", ", pk.Select(c => Sql.Q(c.Name.Trim())))})");

        return $"CREATE TABLE {table} (\n{string.Join(",\n", lines)}\n);";
    }

    /// <summary>
    /// ALTER statements that turn <paramref name="original"/> into the designed table, in one transaction.
    /// Returns an empty string when nothing changed.
    /// </summary>
    public static string AlterTableFromDesign(TableDefinition original, string schema, string name, IReadOnlyList<DesignColumn> columns)
    {
        var table = original.FullName;
        var statements = new List<string>();
        var pk = original.PrimaryKey;
        var originalPk = new HashSet<string>(pk?.Columns.Select(c => c.Name) ?? [], StringComparer.OrdinalIgnoreCase);

        static bool Same(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        static string NormalizeDefault(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : Wrap(value).Replace(" ", "");

        bool TypeChanged(DesignColumn c) =>
            c.Original is { IsComputed: false } o && !c.Removed &&
            (!Same(c.TypeSql, o.TypeSql) || c.AllowNulls != o.IsNullable);

        bool DefaultChanged(DesignColumn c) =>
            c.Original is { } o && !c.Removed &&
            !string.Equals(NormalizeDefault(c.Default), NormalizeDefault(o.DefaultDefinition), StringComparison.OrdinalIgnoreCase);

        var live = columns.Where(c => !c.Removed && !string.IsNullOrWhiteSpace(c.Name)).ToList();
        var pkChanged =
            columns.Any(c => c.Original is { } o && (c.Removed ? originalPk.Contains(o.Name) : c.IsPrimaryKey != originalPk.Contains(o.Name))) ||
            live.Any(c => c.IsNew && c.IsPrimaryKey);
        var pkColumnAltered = columns.Any(c => TypeChanged(c) && originalPk.Contains(c.Original!.Name));
        var dropPk = pk is not null && (pkChanged || pkColumnAltered);
        var newPk = live.Where(c => c.IsPrimaryKey).ToList();
        var addPk = newPk.Count > 0 && (pk is null || dropPk);

        if (dropPk)
            statements.Add($"ALTER TABLE {table} DROP CONSTRAINT {Sql.Q(pk!.Name)};");

        // A default blocks dropping the column or changing its type
        foreach (var c in columns.Where(c => c.Original?.DefaultName is not null && (c.Removed || DefaultChanged(c) || TypeChanged(c))))
            statements.Add($"ALTER TABLE {table} DROP CONSTRAINT {Sql.Q(c.Original!.DefaultName!)};");

        foreach (var c in columns.Where(c => c.Original is not null && !c.Removed && !string.Equals(c.Name.Trim(), c.Original.Name, StringComparison.Ordinal)))
            statements.Add($"EXEC sp_rename {Sql.Lit($"{table}.{Sql.Q(c.Original!.Name)}")}, {Sql.Lit(c.Name.Trim())}, 'COLUMN';");

        foreach (var c in columns.Where(c => c.Original is not null && c.Removed))
            statements.Add($"ALTER TABLE {table} DROP COLUMN {Sql.Q(c.Original!.Name)};");

        foreach (var c in columns.Where(TypeChanged))
        {
            var collate = c.Original!.Collation is not null && IsTextType(c.DataType) ? $" COLLATE {c.Original.Collation}" : "";
            var nulls = c.AllowNulls && !c.IsPrimaryKey ? "NULL" : "NOT NULL";
            statements.Add($"ALTER TABLE {table} ALTER COLUMN {Sql.Q(c.Name.Trim())} {c.TypeSql}{collate} {nulls};");
        }

        foreach (var c in live.Where(c => c.IsNew))
        {
            var sql = $"ALTER TABLE {table} ADD {Sql.Q(c.Name.Trim())} {c.TypeSql}";
            if (c.IsIdentity)
                sql += $" IDENTITY({c.Seed},{c.Increment})";
            sql += c.AllowNulls && !c.IsPrimaryKey ? " NULL" : " NOT NULL";
            if (!string.IsNullOrWhiteSpace(c.Default))
                sql += $" CONSTRAINT {Sql.Q($"DF_{original.Name}_{c.Name.Trim()}")} DEFAULT {Wrap(c.Default)}";
            statements.Add(sql + ";");
        }

        foreach (var c in live.Where(c => !c.IsNew && !string.IsNullOrWhiteSpace(c.Default) &&
                                          (DefaultChanged(c) || (TypeChanged(c) && c.Original!.DefaultName is not null))))
        {
            var defaultName = c.Original!.DefaultName ?? $"DF_{original.Name}_{c.Name.Trim()}";
            statements.Add(AddDefault(table, defaultName, c.Default!, c.Name.Trim()));
        }

        if (addPk)
            statements.Add($"ALTER TABLE {table} ADD CONSTRAINT {Sql.Q(pk?.Name ?? $"PK_{original.Name}")} PRIMARY KEY ({string.Join(", ", newPk.Select(c => Sql.Q(c.Name.Trim())))});");

        var current = table;
        if (!string.Equals(name.Trim(), original.Name, StringComparison.Ordinal))
        {
            statements.Add($"EXEC sp_rename {Sql.Lit(table)}, {Sql.Lit(name.Trim())};");
            current = Sql.Q(original.Schema, name.Trim());
        }

        if (!string.Equals(schema.Trim(), original.Schema, StringComparison.OrdinalIgnoreCase))
            statements.Add($"ALTER SCHEMA {Sql.Q(schema.Trim())} TRANSFER {current};");

        if (statements.Count == 0)
            return string.Empty;

        return "SET XACT_ABORT ON;\nBEGIN TRANSACTION;\n\n" + string.Join("\n", statements) + "\n\nCOMMIT TRANSACTION;";
    }

    /// <summary>EXEC script for "Execute Stored Procedure", like SSMS generates.</summary>
    public static string ExecuteProcedure(string procedure, IReadOnlyList<ParameterDefinition> parameters, IReadOnlyDictionary<string, string?> values)
    {
        var inputs = parameters.Where(p => p.Id > 0).ToList();
        var outputs = inputs.Where(p => p.IsOutput).ToList();
        var sb = new StringBuilder();
        sb.Append("DECLARE @return_value int");
        foreach (var p in outputs)
            sb.Append($",\n        {p.Name} {p.TypeSql}");
        sb.Append(";\n\n");

        sb.Append($"EXEC @return_value = {procedure}");
        var args = inputs.Select(p =>
        {
            if (p.IsOutput)
                return $"{p.Name} = {p.Name} OUTPUT";
            var value = values.GetValueOrDefault(p.Name);
            return $"{p.Name} = {ParameterLiteral(value, p.BaseType)}";
        }).ToList();
        if (args.Count > 0)
            sb.Append("\n        ").Append(string.Join(",\n        ", args));
        sb.Append(";\n\n");

        foreach (var p in outputs)
            sb.Append($"SELECT {p.Name} AS {Sql.Lit(p.Name)};\n");
        sb.Append("SELECT 'Return Value' = @return_value;\n");
        return sb.ToString();
    }

    /// <summary>Numbers stay as typed; everything else becomes a string literal. null → NULL.</summary>
    public static string ParameterLiteral(string? value, string baseType)
    {
        if (value is null)
            return "NULL";
        var numeric = baseType is "int" or "bigint" or "smallint" or "tinyint" or "bit" or "decimal" or "numeric"
            or "money" or "smallmoney" or "float" or "real";
        return numeric && decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            ? value.Trim()
            : Sql.Lit(value);
    }

    public static string NewViewTemplate(string schema = "dbo") => $"""
        CREATE VIEW {Sql.Q(schema, "ViewName")}
        AS
        SELECT
            -- columns
        FROM {Sql.Q(schema, "TableName")};
        GO
        """;

    public static string NewProcedureTemplate(string schema = "dbo") => $"""
        CREATE PROCEDURE {Sql.Q(schema, "ProcedureName")}
            @Param1 int = 0,
            @Param2 nvarchar(50) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT @Param1, @Param2;
        END
        GO
        """;

    public static string NewScalarFunctionTemplate(string schema = "dbo") => $"""
        CREATE FUNCTION {Sql.Q(schema, "FunctionName")}
        (
            @Param1 int
        )
        RETURNS int
        AS
        BEGIN
            DECLARE @Result int;

            SELECT @Result = @Param1;

            RETURN @Result;
        END
        GO
        """;

    public static string NewInlineFunctionTemplate(string schema = "dbo") => $"""
        CREATE FUNCTION {Sql.Q(schema, "FunctionName")}
        (
            @Param1 int
        )
        RETURNS TABLE
        AS
        RETURN
        (
            SELECT @Param1 AS Value
        );
        GO
        """;

    public static string NewTableFunctionTemplate(string schema = "dbo") => $"""
        CREATE FUNCTION {Sql.Q(schema, "FunctionName")}
        (
            @Param1 int
        )
        RETURNS @Result TABLE
        (
            Id int,
            Name nvarchar(100)
        )
        AS
        BEGIN
            INSERT INTO @Result (Id, Name)
            SELECT @Param1, N'Name';

            RETURN;
        END
        GO
        """;

    public static string NewTriggerTemplate(string table, string tableName) => $"""
        CREATE TRIGGER {Sql.Q("TR_" + tableName + "_Change")}
        ON {table}
        AFTER INSERT, UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;

            -- inserted / deleted hold the changed rows
        END
        GO
        """;

    public static string NewTypeTemplate(string schema = "dbo") => $"""
        -- Alias type
        CREATE TYPE {Sql.Q(schema, "TypeName")} FROM nvarchar(50) NOT NULL;
        GO

        -- Table type
        CREATE TYPE {Sql.Q(schema, "TableTypeName")} AS TABLE
        (
            Id int NOT NULL PRIMARY KEY,
            Name nvarchar(100) NULL
        );
        GO
        """;
}
