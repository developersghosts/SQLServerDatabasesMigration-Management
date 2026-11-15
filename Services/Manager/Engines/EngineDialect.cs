using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MigrateSQLServerDatabases.Models;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>One statement of a script, with the editor line it starts on.</summary>
public sealed record SqlStatement(string Text, int StartLine);

/// <summary>A table column with its keys, as read from the engine's catalog.</summary>
public sealed record ColumnKeysInfo(
    string Name,
    string Type,
    bool Nullable,
    bool PrimaryKey,
    bool Unique,
    (string Schema, string Table, string Column)? ForeignKey,
    bool Identity,
    bool Computed);

/// <summary>The dialect of each engine other than SQL Server (which has its own services).</summary>
public static class Dialects
{
    private static readonly EngineDialect MySql = new MySqlDialect();
    private static readonly EngineDialect Postgres = new PostgresDialect();
    private static readonly EngineDialect Oracle = new OracleDialect();

    public static EngineDialect For(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.MySql => MySql,
        DatabaseEngine.PostgreSql => Postgres,
        DatabaseEngine.Oracle => Oracle,
        _ => throw new NotSupportedException("SQL Server uses its own services.")
    };

    public static EngineDialect For(ManagedServer server) => For(server.Engine);
}

/// <summary>
/// Everything the manager needs to work with MySQL, PostgreSQL or Oracle: connections, quoting, the Object Explorer,
/// scripting, statement splitting and value conversion.
/// </summary>
public abstract partial class EngineDialect
{
    public abstract DatabaseEngine Engine { get; }
    public abstract string Product { get; }
    public abstract int DefaultPort { get; }

    /// <summary>Label of the database box in the connect dialog ("Database", "Service name").</summary>
    public virtual string DatabaseLabelKey => "Database";
    public virtual string? DefaultDatabase => null;

    protected virtual char QuoteOpen => '"';
    protected virtual char QuoteClose => '"';
    protected virtual string ParameterPrefix => "@";

    // ---------- Connections ----------

    public abstract DbConnection CreateConnection(string connectionString);

    public abstract string BuildConnectionString(string host, int port, string user, string password, string? database);

    /// <summary>Name to show for the server, taken from a connection string.</summary>
    public abstract string ServerName(string connectionString);

    protected abstract string CurrentUserSql { get; }

    public async Task<DbConnection> OpenAsync(ManagedServer server, string? database, CancellationToken cancellationToken = default) =>
        await OpenAsync(server.ConnectionStringFor(database), database, cancellationToken);

    public async Task<DbConnection> OpenAsync(string connectionString, string? database = null, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await AfterOpenAsync(connection, database, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Session setup after connecting. Oracle switches to the schema here; the others pick the database in the connection string.</summary>
    protected virtual Task AfterOpenAsync(DbConnection connection, string? database, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Connects and reads the version and the signed-in user.</summary>
    public async Task<(ServerInfo Info, string Login)> GetServerInfoAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = Command(connection, CurrentUserSql);
        var login = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
        var version = connection.ServerVersion;
        var info = new ServerInfo(ServerName(connectionString), connection.Database, version, string.Empty, Product, -1);
        return (info, login);
    }

    public DbCommand Command(DbConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        PrepareCommand(command);
        foreach (var (name, value) in args)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>Oracle binds parameters by name.</summary>
    protected virtual void PrepareCommand(DbCommand command)
    {
    }

    public async Task<List<T>> QueryAsync<T>(ManagedServer server, string? database, string sql, Func<DbDataReader, T> map,
        params (string Name, object? Value)[] args)
    {
        await using var connection = await OpenAsync(server, database);
        await using var command = Command(connection, sql, args);
        await using var reader = await command.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync())
            list.Add(map(reader));
        return list;
    }

    public async Task<object?> ScalarAsync(ManagedServer server, string? database, string sql, params (string Name, object? Value)[] args)
    {
        await using var connection = await OpenAsync(server, database);
        await using var command = Command(connection, sql, args);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    // ---------- Quoting ----------

    public string Q(string? identifier) =>
        QuoteOpen + (identifier ?? string.Empty).Replace(QuoteClose.ToString(), new string(QuoteClose, 2)) + QuoteClose;

    public string Name(string? schema, string? name) => string.IsNullOrEmpty(schema) ? Q(name) : $"{Q(schema)}.{Q(name)}";

    public virtual string Literal(string? value) => value is null ? "NULL" : "'" + value.Replace("'", "''") + "'";

    /// <summary>Parameter as written in SQL (@name, :name).</summary>
    public string P(string name) => ParameterPrefix + name;

    protected abstract string Limit(int top);

    public string SelectTop(string table, IEnumerable<string> columns, int top) =>
        $"SELECT {string.Join("\n      ,", columns.Select(Q))}\n  FROM {table}{Limit(top)};";

    public string SelectWhere(string table, string column, string value, int top) =>
        $"SELECT *\n  FROM {table}\n WHERE {Q(column)} = {Literal(value)}{Limit(top)};";

    // ---------- Object Explorer ----------

    public abstract List<ExplorerNode> ServerFolders(ExplorerNode server);

    public abstract Task<List<ExplorerNode>> LoadChildrenAsync(ExplorerNode node);

    /// <summary>Databases (MySQL, PostgreSQL) or schemas (Oracle) for the query tab's list.</summary>
    public abstract Task<List<string>> GetDatabaseNamesAsync(ManagedServer server);

    /// <summary>A table's columns in order, with their keys.</summary>
    public abstract Task<List<ColumnKeysInfo>> LoadColumnsAsync(ManagedServer server, string? database, string? schema, string table);

    protected static ExplorerNode Folder(ExplorerNode parent, NodeKind kind) => new()
    {
        Kind = kind,
        Server = parent.Server,
        Parent = parent,
        LabelKey = $"Folder{kind.ToString().Replace("Folder", "")}",
        Database = parent.Database,
        Schema = parent.Schema,
        ParentSchema = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Schema : parent.ParentSchema,
        ParentName = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Name : parent.ParentName,
        ParentKind = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Kind : parent.ParentKind,
        CanExpand = true
    };

    protected static List<ExplorerNode> Folders(ExplorerNode parent, params NodeKind[] kinds) => kinds.Select(k => Folder(parent, k)).ToList();

    protected static ExplorerNode Item(ExplorerNode parent, NodeKind kind, string? schema, string name, string? label = null,
        string? detail = null, string? subType = null, bool canExpand = false, string? database = null, long? rows = null,
        bool system = false, bool disabled = false) => new()
    {
        Kind = kind,
        Server = parent.Server,
        Parent = parent,
        Label = label ?? name,
        Database = database ?? parent.Database,
        Schema = schema,
        Name = name,
        Detail = detail,
        SubType = subType,
        ParentSchema = parent.ParentSchema,
        ParentName = parent.ParentName,
        ParentKind = parent.ParentKind,
        CanExpand = canExpand,
        RowCount = rows,
        IsSystem = system,
        IsDisabled = disabled
    };

    /// <summary>Column nodes with "PK, FK, type, null" details, like the SQL Server tree.</summary>
    protected async Task<List<ExplorerNode>> ColumnNodesAsync(ExplorerNode folder)
    {
        var columns = await LoadColumnsAsync(folder.Server, folder.Database, folder.ParentSchema, folder.ParentName!);
        return columns.Select(c =>
        {
            var parts = new List<string>();
            if (c.PrimaryKey) parts.Add("PK");
            if (c.ForeignKey is not null) parts.Add("FK");
            if (c.Computed) parts.Add("Computed");
            if (c.Identity) parts.Add("Identity");
            parts.Add(c.Type);
            parts.Add(c.Nullable ? "null" : "not null");
            return Item(folder, NodeKind.Column, null, c.Name, detail: string.Join(", ", parts),
                subType: c.PrimaryKey ? "PK" : c.ForeignKey is not null ? "FK" : null);
        }).ToList();
    }

    protected static bool Bool(DbDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return false;
        return reader.GetValue(i) switch
        {
            bool b => b,
            string s => s.Trim().ToUpperInvariant() is "Y" or "YES" or "1" or "TRUE" or "ENABLED",
            var v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0
        };
    }

    protected static string Str(DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;

    protected static string? NStr(DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);

    protected static long? NLong(DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);

    // ---------- Query tab ----------

    /// <summary>Switches the session's database or schema; false means the tab must reconnect (PostgreSQL).</summary>
    public abstract Task<bool> ChangeDatabaseAsync(DbConnection connection, string database);

    public virtual Task<string?> CurrentDatabaseAsync(DbConnection connection) => Task.FromResult<string?>(connection.Database);

    /// <summary>Forwards the server's notices (PRINT-like output) to the messages pane.</summary>
    public virtual IDisposable? HookMessages(DbConnection connection, Action<string> message) => null;

    // ---------- Scripting ----------

    public abstract Task<string> ScriptCreateAsync(ExplorerNode node);

    /// <summary>DROP statement for a node, or null when the engine can't drop it this way.</summary>
    public abstract string? DropSql(ExplorerNode node);

    public abstract string? RenameSql(ExplorerNode node, string newName);

    public virtual string TruncateSql(ExplorerNode node) => $"TRUNCATE TABLE {node.QualifiedName};";

    /// <summary>CREATE DATABASE (MySQL, PostgreSQL); null for Oracle.</summary>
    public abstract string? CreateDatabaseSql(string name);

    /// <summary>CREATE SCHEMA (PostgreSQL); null where schemas are databases or users.</summary>
    public virtual string? CreateSchemaSql(string name) => null;

    public abstract string NewTableTemplate(string? schema);
    public abstract string NewViewTemplate(string? schema);
    public abstract string NewProcedureTemplate(string? schema);
    public abstract string NewFunctionTemplate(string? schema);

    /// <summary>Sessions on the server, with how to end one.</summary>
    public abstract string ActivitySql { get; }

    /// <summary>Server version and settings.</summary>
    public abstract string SettingsSql { get; }

    // ---------- Values ----------

    /// <summary>
    /// Typed parameter value for grid text, by the column's type name. "NULL" (and an empty non-text cell) is NULL.
    /// </summary>
    /// <param name="nullWord">Treat the text NULL as NULL; off for key values read from the grid.</param>
    public virtual object ToValue(string? text, string type, bool nullWord = true)
    {
        if (text is null || (nullWord && text == "NULL"))
            return DBNull.Value;

        var t = type.ToLowerInvariant();
        var isText = t.Contains("char") || t.Contains("text") || t.Contains("clob") || t.Contains("json") || t.Contains("xml") || t.Contains("enum") || t.Contains("set");
        if (!isText && string.IsNullOrWhiteSpace(text))
            return DBNull.Value;

        var s = text.Trim();
        var inv = CultureInfo.InvariantCulture;
        try
        {
            if (isText)
                return text;
            if (t is "uuid")
                return Guid.Parse(s);
            if (t.StartsWith("bool", StringComparison.Ordinal))
                return s is "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("t", StringComparison.OrdinalIgnoreCase);
            if (t.Contains("interval"))
                return text;
            if (Regex.IsMatch(t, @"^(tiny|small|medium|big)?int(eger)?\b|^int[248]\b|^(big|small)?serial\b"))
                return long.Parse(s, NumberStyles.Integer, inv);
            if (t.StartsWith("float", StringComparison.Ordinal) || t.StartsWith("double", StringComparison.Ordinal) || t.StartsWith("real", StringComparison.Ordinal) || t.StartsWith("binary_", StringComparison.Ordinal))
                return double.Parse(s, NumberStyles.Float, inv);
            if (t.StartsWith("decimal", StringComparison.Ordinal) || t.StartsWith("numeric", StringComparison.Ordinal) || t.StartsWith("number", StringComparison.Ordinal) || t.StartsWith("money", StringComparison.Ordinal) || t.StartsWith("dec", StringComparison.Ordinal))
                return decimal.Parse(s, NumberStyles.Number | NumberStyles.AllowExponent, inv);
            if (t.StartsWith("time", StringComparison.Ordinal) && !t.StartsWith("timestamp", StringComparison.Ordinal))
                return TimeSpan.Parse(s, inv);
            if (t.Contains("with time zone") || t is "timestamptz")
                return DateTime.SpecifyKind(DateTime.Parse(s, inv), DateTimeKind.Utc);
            if (t.StartsWith("date", StringComparison.Ordinal) || t.StartsWith("timestamp", StringComparison.Ordinal))
                return DateTime.Parse(s, inv);
            if (t is "bytea" or "raw" or "blob" or "longblob" or "mediumblob" or "tinyblob" || t.StartsWith("binary", StringComparison.Ordinal) || t.StartsWith("varbinary", StringComparison.Ordinal) || t.StartsWith("raw", StringComparison.Ordinal))
                return SqlValues.ParseHex(s);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FormatException($"\"{text}\" is not a valid {type} value.", ex);
        }

        return text;
    }

    // ---------- Statement splitting ----------

    /// <summary>Quote characters that start a string or identifier.</summary>
    protected virtual string Quotes => "'\"";
    protected virtual bool HashComments => false;
    protected virtual bool BackslashEscapes => false;
    protected virtual bool DollarQuotes => false;
    protected virtual bool DelimiterCommand => false;
    protected virtual bool SlashTerminator => false;

    /// <summary>Oracle: a PL/SQL block keeps its semicolons and ends at a line holding only "/".</summary>
    protected virtual bool IsBlockStart(string code) => false;

    [GeneratedRegex(@"^\$[A-Za-z_][A-Za-z0-9_]*\$|^\$\$")]
    private static partial Regex DollarTag();

    /// <summary>
    /// Splits a script into statements on the delimiter (";" or MySQL's DELIMITER), outside strings, identifiers,
    /// comments, PostgreSQL $$ bodies and Oracle PL/SQL blocks.
    /// </summary>
    public List<SqlStatement> Split(string script)
    {
        var text = script.Replace("\r\n", "\n").Replace('\r', '\n');
        var statements = new List<SqlStatement>();
        var current = new StringBuilder();
        var code = new StringBuilder();
        var delimiter = ";";
        var line = 1;
        var startLine = 0;
        var block = false;
        var i = 0;

        void Emit()
        {
            var statement = current.ToString().Trim();
            if (code.ToString().Trim().Length > 0 && statement.Length > 0)
                statements.Add(new SqlStatement(statement, startLine == 0 ? line : startLine));
            current.Clear();
            code.Clear();
            startLine = 0;
            block = false;
        }

        void Append(char c, bool isCode)
        {
            if (isCode && !char.IsWhiteSpace(c) && startLine == 0)
                startLine = line;
            current.Append(c);
            if (isCode)
                code.Append(c);
            if (c == '\n')
                line++;
        }

        while (i < text.Length)
        {
            var atLineStart = i == 0 || text[i - 1] == '\n';
            if (atLineStart)
            {
                var end = text.IndexOf('\n', i);
                var lineText = (end < 0 ? text[i..] : text[i..end]).Trim();

                if (DelimiterCommand && code.ToString().Trim().Length == 0 &&
                    lineText.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
                {
                    delimiter = lineText[10..].Trim();
                    current.Clear();
                    code.Clear();
                    i = end < 0 ? text.Length : end + 1;
                    line++;
                    continue;
                }

                if (SlashTerminator && lineText == "/")
                {
                    Emit();
                    i = end < 0 ? text.Length : end + 1;
                    line++;
                    continue;
                }
            }

            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            // Comments
            if ((c == '-' && next == '-') || (HashComments && c == '#'))
            {
                while (i < text.Length && text[i] != '\n')
                    Append(text[i++], false);
                continue;
            }

            if (c == '/' && next == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? text.Length : close + 2;
                while (i < stop)
                    Append(text[i++], false);
                continue;
            }

            // Strings and quoted identifiers
            if (Quotes.Contains(c) || (Engine == DatabaseEngine.MySql && c == '`'))
            {
                Append(c, true);
                i++;
                while (i < text.Length)
                {
                    var q = text[i];
                    if (BackslashEscapes && q == '\\' && c != '`' && i + 1 < text.Length)
                    {
                        Append(q, true);
                        Append(text[i + 1], true);
                        i += 2;
                        continue;
                    }

                    Append(q, true);
                    i++;
                    if (q == c)
                    {
                        if (i < text.Length && text[i] == c)
                        {
                            Append(text[i], true);
                            i++;
                            continue;
                        }

                        break;
                    }
                }

                continue;
            }

            // PostgreSQL dollar-quoted bodies: $$ ... $$ or $tag$ ... $tag$
            if (DollarQuotes && c == '$')
            {
                var tag = DollarTag().Match(text[i..Math.Min(text.Length, i + 64)]);
                if (tag.Success)
                {
                    var close = text.IndexOf(tag.Value, i + tag.Value.Length, StringComparison.Ordinal);
                    var stop = close < 0 ? text.Length : close + tag.Value.Length;
                    while (i < stop)
                        Append(text[i++], true);
                    continue;
                }
            }

            if (!block && string.CompareOrdinal(text, i, delimiter, 0, delimiter.Length) == 0)
            {
                if (SlashTerminator && IsBlockStart(code.ToString()))
                {
                    // Inside PL/SQL: keep the semicolon, the block ends at "/"
                    block = true;
                    Append(c, true);
                    i++;
                    continue;
                }

                Emit();
                i += delimiter.Length;
                continue;
            }

            Append(c, true);
            i++;
        }

        Emit();
        return statements;
    }
}
