using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>Converts values between SQL Server, the grids and T-SQL literals.</summary>
public static class SqlValues
{
    private const int MaxDisplayChars = 65535;
    private const int MaxDisplayBytes = 32768;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Formats a value for the results grid, like SSMS.</summary>
    public static string Display(object value, string typeName) => value switch
    {
        string s => s.Length > MaxDisplayChars ? s[..MaxDisplayChars] : s,
        byte[] bytes => Hex(bytes, MaxDisplayBytes),
        bool b => b ? "1" : "0",
        DateTime d => typeName switch
        {
            "date" => d.ToString("yyyy-MM-dd", Inv),
            "smalldatetime" => d.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            "datetime" => d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv),
            _ => d.ToString("yyyy-MM-dd HH:mm:ss.fffffff", Inv)
        },
        DateTimeOffset o => o.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", Inv),
        TimeSpan t => t.ToString(@"hh\:mm\:ss\.fffffff", Inv),
        Guid g => g.ToString().ToUpperInvariant(),
        double f => f.ToString("R", Inv),
        float f => f.ToString("R", Inv),
        IFormattable f => f.ToString(null, Inv),
        // PostgreSQL arrays
        Array array => "{" + string.Join(",", array.Cast<object?>().Select(v => v is null ? "NULL" : Display(v, string.Empty))) + "}",
        _ => Convert.ToString(value, Inv) ?? string.Empty
    };

    public static string Hex(byte[] bytes, int max = int.MaxValue)
    {
        var shown = bytes.Length > max ? bytes.AsSpan(0, max) : bytes;
        return "0x" + Convert.ToHexString(shown) + (bytes.Length > max ? "…" : string.Empty);
    }

    /// <summary>Text shown in an editable cell; it parses back with <see cref="Parse"/>.</summary>
    public static string? ToEditText(object? value, string type) => value switch
    {
        null => null,
        byte[] bytes => Hex(bytes),
        bool b => b ? "True" : "False",
        DateTime d => type == "date" ? d.ToString("yyyy-MM-dd", Inv) : d.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", Inv),
        DateTimeOffset o => o.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", Inv),
        TimeSpan t => t.ToString("c", Inv),
        double f => f.ToString("R", Inv),
        float f => f.ToString("R", Inv),
        IFormattable f => f.ToString(null, Inv),
        _ => Convert.ToString(value, Inv)
    };

    /// <summary>
    /// Parses grid input for a column type. "NULL" means NULL, as in SSMS; an empty cell is NULL
    /// for non-text columns. Throws FormatException for invalid input.
    /// </summary>
    /// <param name="nullWord">Treat the text NULL as SQL NULL (typed input); off for values read from the grid.</param>
    public static object Parse(string? text, string type, bool isText, bool nullWord = true)
    {
        if (text is null || (nullWord && text == "NULL"))
            return DBNull.Value;
        if (!isText && string.IsNullOrWhiteSpace(text))
            return DBNull.Value;

        var t = text.Trim();
        return type switch
        {
            "bigint" => long.Parse(t, NumberStyles.Integer, Inv),
            "int" => int.Parse(t, NumberStyles.Integer, Inv),
            "smallint" => short.Parse(t, NumberStyles.Integer, Inv),
            "tinyint" => byte.Parse(t, NumberStyles.Integer, Inv),
            "bit" => t switch
            {
                "1" => true,
                "0" => false,
                _ => bool.Parse(t)
            },
            "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(t, NumberStyles.Number | NumberStyles.AllowExponent, Inv),
            "float" => double.Parse(t, NumberStyles.Float, Inv),
            "real" => float.Parse(t, NumberStyles.Float, Inv),
            "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(t, Inv, DateTimeStyles.None),
            "datetimeoffset" => DateTimeOffset.Parse(t, Inv, DateTimeStyles.None),
            "time" => TimeSpan.Parse(t, Inv),
            "uniqueidentifier" => Guid.Parse(t),
            "binary" or "varbinary" or "image" or "timestamp" => ParseHex(t),
            _ => text
        };
    }

    public static byte[] ParseHex(string text)
    {
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (hex.Length % 2 == 1)
            hex = "0" + hex;
        return Convert.FromHexString(hex);
    }

    /// <summary>SqlDbType for a system type, so dates keep their precision and range.</summary>
    public static SqlParameter Parameter(string name, object value, string type)
    {
        var parameter = new SqlParameter(name, value ?? DBNull.Value);
        SqlDbType? dbType = type switch
        {
            "bigint" => SqlDbType.BigInt,
            "int" => SqlDbType.Int,
            "smallint" => SqlDbType.SmallInt,
            "tinyint" => SqlDbType.TinyInt,
            "bit" => SqlDbType.Bit,
            "decimal" or "numeric" => SqlDbType.Decimal,
            "money" => SqlDbType.Money,
            "smallmoney" => SqlDbType.SmallMoney,
            "float" => SqlDbType.Float,
            "real" => SqlDbType.Real,
            "date" => SqlDbType.Date,
            "datetime" => SqlDbType.DateTime,
            "datetime2" => SqlDbType.DateTime2,
            "smalldatetime" => SqlDbType.SmallDateTime,
            "time" => SqlDbType.Time,
            "datetimeoffset" => SqlDbType.DateTimeOffset,
            "uniqueidentifier" => SqlDbType.UniqueIdentifier,
            "binary" or "timestamp" => SqlDbType.Binary,
            "varbinary" => SqlDbType.VarBinary,
            "image" => SqlDbType.Image,
            "char" => SqlDbType.Char,
            "varchar" => SqlDbType.VarChar,
            "text" => SqlDbType.Text,
            "nchar" => SqlDbType.NChar,
            "nvarchar" => SqlDbType.NVarChar,
            "ntext" => SqlDbType.NText,
            "xml" => SqlDbType.Xml,
            _ => null
        };

        if (dbType is { } db)
        {
            parameter.SqlDbType = db;
            if (db is SqlDbType.DateTime2 or SqlDbType.Time or SqlDbType.DateTimeOffset)
                parameter.Scale = 7;
            if (value is string s && db is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar)
                parameter.Size = s.Length > 4000 ? -1 : Math.Max(s.Length, 1);
            if (value is byte[] b && db is SqlDbType.Binary or SqlDbType.VarBinary)
                parameter.Size = b.Length > 8000 ? -1 : Math.Max(b.Length, 1);
        }

        return parameter;
    }

    /// <summary>T-SQL literal for a value read from a column, for INSERT scripts.</summary>
    public static string Literal(object? value, string type) => value switch
    {
        null or DBNull => "NULL",
        string s => type is "char" or "varchar" or "text" ? "'" + s.Replace("'", "''") + "'" : Sql.Lit(s),
        bool b => b ? "1" : "0",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        DateTime d => "'" + type switch
        {
            "date" => d.ToString("yyyy-MM-dd", Inv),
            "smalldatetime" => d.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
            "datetime" => d.ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv),
            _ => d.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv)
        } + "'",
        DateTimeOffset o => "'" + o.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", Inv) + "'",
        TimeSpan t => "'" + t.ToString(@"hh\:mm\:ss\.fffffff", Inv) + "'",
        Guid g => "'" + g + "'",
        double f => f.ToString("R", Inv),
        float f => f.ToString("R", Inv),
        IFormattable f => f.ToString(null, Inv),
        _ => Sql.Lit(Convert.ToString(value, Inv))
    };

    /// <summary>Tab-separated text with a header row, for the clipboard.</summary>
    public static string ToTsv(Models.Manager.ResultSet set, bool headers = true)
    {
        var sb = new StringBuilder();
        if (headers)
            sb.AppendJoin('\t', set.Columns.Select(c => c.Name)).Append("\r\n");
        foreach (var row in set.Rows)
            sb.AppendJoin('\t', row.Select(v => v?.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ") ?? "NULL")).Append("\r\n");
        return sb.ToString();
    }

    public static string ToCsv(Models.Manager.ResultSet set)
    {
        static string Cell(string? value)
        {
            if (value is null)
                return string.Empty;
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        var sb = new StringBuilder();
        sb.AppendJoin(',', set.Columns.Select(c => Cell(c.Name))).Append("\r\n");
        foreach (var row in set.Rows)
            sb.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        return sb.ToString();
    }
}
