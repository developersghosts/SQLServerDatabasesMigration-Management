using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>
/// Runs scripts on MySQL, PostgreSQL and Oracle: one statement at a time (split by the dialect), several result sets,
/// rows-affected messages, server notices, errors with their line, and cancellation.
/// Each statement runs on a worker thread (Oracle's driver blocks), and its grids are added when it finishes.
/// </summary>
public static class GenericQueryRunner
{
    public static async Task RunAsync(EngineDialect dialect, DbConnection connection, string script, QueryExecution execution,
        RunOptions options, CancellationToken cancellationToken)
    {
        var statements = dialect.Split(script);
        using var notices = dialect.HookMessages(connection, text =>
        {
            execution.Enqueue(new QueryMessage(MessageKind.Info, text));
            options.OnBackgroundMessage?.Invoke();
        });

        execution.Started = DateTime.Now;
        var watch = Stopwatch.StartNew();
        try
        {
            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (connection.State != ConnectionState.Open)
                {
                    execution.AddError("The connection was closed by the server.");
                    return;
                }

                // MySQL's USE and Oracle's ALTER SESSION change it between statements
                var database = await dialect.CurrentDatabaseAsync(connection) ?? connection.Database;
                var outcome = await Task.Run(() => RunStatement(dialect, connection, statement, database, options, cancellationToken), cancellationToken);
                execution.Results.AddRange(outcome.Results);
                execution.DrainPending();
                foreach (var message in outcome.Messages)
                    execution.Enqueue(message);
                execution.DrainPending();

                if (options.OnProgress is not null)
                    await options.OnProgress();
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            execution.Cancelled = true;
        }
        catch (Exception ex)
        {
            execution.AddError(ex.Message);
        }
        finally
        {
            watch.Stop();
            execution.Elapsed = watch.Elapsed;
            execution.DrainPending();
            execution.Completed = true;
        }
    }

    private sealed record Outcome(List<ResultSet> Results, List<QueryMessage> Messages);

    private static Outcome RunStatement(EngineDialect dialect, DbConnection connection, SqlStatement statement, string database,
        RunOptions options, CancellationToken cancellationToken)
    {
        var results = new List<ResultSet>();
        var messages = new List<QueryMessage>();
        using var command = dialect.Command(connection, statement.Text);
        command.CommandTimeout = options.CommandTimeout;
        using var cancel = cancellationToken.Register(() =>
        {
            try
            {
                command.Cancel();
            }
            catch (Exception)
            {
            }
        });

        try
        {
            using var reader = command.ExecuteReader();
            var index = 0;
            do
            {
                if (reader.FieldCount == 0)
                    continue;

                var set = ReadResult(reader, statement, database, index++, options, cancellationToken);
                results.Add(set);
                messages.Add(new QueryMessage(MessageKind.RowsAffected, set.TotalRows == 1 ? "(1 row)" : $"({set.TotalRows:N0} rows)"));
            }
            while (reader.NextResult());

            reader.Close();
            if (results.Count == 0 && reader.RecordsAffected >= 0)
                messages.Add(QueryMessage.Rows(reader.RecordsAffected));
            ResolvePostgresOrigins(connection, results);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (DbException ex)
        {
            var line = ErrorLine(ex, statement, options.LineOffset);
            messages.Add(new QueryMessage(MessageKind.Error, $"Line {line}: {ErrorText(ex)}", line));
        }

        return new Outcome(results, messages);
    }

    private static string ErrorText(DbException ex) => ex is Npgsql.PostgresException pg
        ? $"{pg.SqlState}: {pg.MessageText}" + (pg.Detail is { Length: > 0 } d ? $"\n{d}" : "") + (pg.Hint is { Length: > 0 } h ? $"\nHINT: {h}" : "")
        : ex.Message;

    /// <summary>PostgreSQL reports the character position of an error; the others report the statement.</summary>
    private static int ErrorLine(DbException ex, SqlStatement statement, int offset)
    {
        var line = statement.StartLine + offset;
        if (ex is Npgsql.PostgresException { Position: > 0 } pg && pg.Position <= statement.Text.Length)
            line += statement.Text.AsSpan(0, pg.Position - 1).Count('\n');
        return line;
    }

    private static ResultSet ReadResult(DbDataReader reader, SqlStatement statement, string database, int index, RunOptions options,
        CancellationToken cancellationToken)
    {
        var columns = new List<ResultColumn>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            string type;
            try
            {
                type = reader.GetDataTypeName(i).ToLowerInvariant();
            }
            catch (Exception)
            {
                type = string.Empty;
            }

            columns.Add(new ResultColumn(reader.GetName(i), type));
        }

        var set = new ResultSet(columns)
        {
            BatchSql = statement.Text,
            Database = database,
            IndexInBatch = index,
            Origins = ReadOrigins(reader)
        };

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            set.TotalRows++;
            if (set.Rows.Count < options.MaxRowsPerResult)
                set.Rows.Add(ReadRow(reader, columns));
            else
                set.Truncated = true;
        }

        return set;
    }

    /// <summary>Base table and column of each result column, used to edit cells and show keys.</summary>
    private static List<ColumnOrigin?>? ReadOrigins(DbDataReader reader)
    {
        // Npgsql only reports the table OID and column number; names are looked up once the reader is closed
        if (reader is Npgsql.NpgsqlDataReader npgsql)
        {
            try
            {
                return npgsql.GetColumnSchema()
                    .Select(c => c.TableOID == 0 || c.ColumnAttributeNumber is not > 0
                        ? null
                        : new ColumnOrigin(PendingOid, c.TableOID.ToString(CultureInfo.InvariantCulture), c.ColumnAttributeNumber.Value.ToString(CultureInfo.InvariantCulture)))
                    .ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        try
        {
            return reader.GetColumnSchema()
                .Select(c => string.IsNullOrEmpty(c.BaseTableName) || string.IsNullOrEmpty(c.BaseColumnName)
                    ? null
                    : new ColumnOrigin(string.IsNullOrEmpty(c.BaseSchemaName) ? null : c.BaseSchemaName, c.BaseTableName, c.BaseColumnName))
                .ToList();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const string PendingOid = "\u0000oid";

    /// <summary>Replaces the PostgreSQL table OIDs and column numbers with schema, table and column names.</summary>
    private static void ResolvePostgresOrigins(DbConnection connection, List<ResultSet> results)
    {
        if (connection is not Npgsql.NpgsqlConnection)
            return;

        var pending = results.Where(r => r.Origins is not null).SelectMany(r => r.Origins!).Where(o => o?.Schema == PendingOid).ToList();
        if (pending.Count == 0)
            return;

        var names = new Dictionary<(string, string), ColumnOrigin>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.attrelid::bigint, a.attnum, n.nspname, c.relname, a.attname
                FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE a.attrelid::bigint = ANY (@oids) AND a.attnum > 0
                """;
            command.Parameters.Add(new Npgsql.NpgsqlParameter("oids", pending.Select(o => long.Parse(o!.Table, CultureInfo.InvariantCulture)).Distinct().ToArray()));
            using var reader = command.ExecuteReader();
            while (reader.Read())
                names[(reader.GetInt64(0).ToString(CultureInfo.InvariantCulture), reader.GetInt16(1).ToString(CultureInfo.InvariantCulture))] =
                    new ColumnOrigin(reader.GetString(2), reader.GetString(3), reader.GetString(4));
        }
        catch (Exception)
        {
        }

        foreach (var set in results)
        {
            if (set.Origins is not { } origins)
                continue;
            for (var i = 0; i < origins.Count; i++)
                if (origins[i] is { Schema: PendingOid } o)
                    origins[i] = names.GetValueOrDefault((o.Table, o.Column));
        }
    }

    private static string?[] ReadRow(DbDataReader reader, List<ResultColumn> columns)
    {
        var row = new string?[columns.Count];
        for (var i = 0; i < row.Length; i++)
        {
            if (reader.IsDBNull(i))
                continue;

            try
            {
                row[i] = SqlValues.Display(reader.GetValue(i), columns[i].TypeName);
            }
            catch (Exception)
            {
                // Values the CLR type can't hold (Oracle NUMBER with 38 digits, MySQL zero dates…) are shown as the provider sees them
                try
                {
                    row[i] = Convert.ToString(reader.GetProviderSpecificValue(i), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    row[i] = "<" + ex.Message + ">";
                }
            }
        }

        return row;
    }
}
