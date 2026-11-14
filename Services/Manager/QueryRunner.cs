using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

public sealed class RunOptions
{
    /// <summary>Lines before the executed text in the editor (when running a selection).</summary>
    public int LineOffset { get; init; }

    /// <summary>Rows kept per grid; the rest are counted but not stored.</summary>
    public int MaxRowsPerResult { get; init; } = 100_000;

    /// <summary>0 = no timeout, like SSMS.</summary>
    public int CommandTimeout { get; init; }

    /// <summary>SET PARSEONLY ON: check the syntax without running anything.</summary>
    public bool ParseOnly { get; init; }

    /// <summary>Called on the render thread every few hundred milliseconds while rows stream in.</summary>
    public Func<Task>? OnProgress { get; init; }

    /// <summary>
    /// Called from a SqlClient thread when the server sends a message (PRINT, BACKUP … STATS);
    /// the caller should dispatch to the render thread and call <see cref="QueryExecution.DrainPending"/>.
    /// </summary>
    public Action? OnBackgroundMessage { get; init; }
}

/// <summary>
/// Runs T-SQL scripts like an SSMS query window: GO batches, several result sets per batch,
/// "(n rows affected)", errors as "Msg n, Level n, State n, Line n", and cancellation.
/// </summary>
public static class QueryRunner
{
    public static async Task RunAsync(SqlConnection connection, string script, QueryExecution execution, RunOptions options, CancellationToken cancellationToken)
    {
        var batches = SqlText.SplitBatches(script);
        var batchLine = 1 + options.LineOffset;

        void OnInfoMessage(object? sender, SqlInfoMessageEventArgs e)
        {
            foreach (SqlError error in e.Errors)
                execution.Enqueue(ToMessage(error, batchLine));
            options.OnBackgroundMessage?.Invoke();
        }

        // Errors up to severity 16 arrive as info messages, so the batch keeps going like in SSMS,
        // and BACKUP/RESTORE ... STATS progress messages arrive while the command runs
        connection.FireInfoMessageEventOnUserErrors = true;
        connection.InfoMessage += OnInfoMessage;
        execution.Started = DateTime.Now;
        var watch = Stopwatch.StartNew();

        try
        {
            if (options.ParseOnly)
                await RunSimpleAsync(connection, "SET PARSEONLY ON", cancellationToken);

            foreach (var batch in batches)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (connection.State != ConnectionState.Open)
                    {
                        execution.AddError("The connection was closed by the server.");
                        return;
                    }

                    batchLine = options.LineOffset + batch.StartLine;
                    await RunBatchAsync(connection, batch.Text, batchLine, execution, options, cancellationToken);
                }
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
            if (options.ParseOnly && connection.State == ConnectionState.Open)
            {
                try
                {
                    await RunSimpleAsync(connection, "SET PARSEONLY OFF", CancellationToken.None);
                }
                catch (SqlException)
                {
                }
            }

            connection.InfoMessage -= OnInfoMessage;
            if (connection.State == ConnectionState.Open)
                connection.FireInfoMessageEventOnUserErrors = false;

            watch.Stop();
            execution.Elapsed = watch.Elapsed;
            execution.DrainPending();
            execution.Completed = true;
        }
    }

    /// <summary>Runs a script on a new connection (dialogs: backup, restore, create, drop…).</summary>
    public static async Task<QueryExecution> RunOnNewConnectionAsync(
        string connectionString, string script, RunOptions options, CancellationToken cancellationToken = default)
    {
        var execution = new QueryExecution();
        try
        {
            await using var connection = await Db.OpenAsync(connectionString, cancellationToken);
            await RunAsync(connection, script, execution, options, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            execution.AddError(ex.Message);
            execution.Completed = true;
        }

        return execution;
    }

    private static async Task RunSimpleAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RunBatchAsync(
        SqlConnection connection, string text, int line, QueryExecution execution, RunOptions options, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(text, connection) { CommandTimeout = options.CommandTimeout };
        command.StatementCompleted += (_, e) => execution.Enqueue(QueryMessage.Rows(e.RecordCount));
        var database = connection.Database;
        var index = 0;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            execution.DrainPending();
            do
            {
                if (reader.FieldCount > 0)
                    await ReadResultAsync(reader, execution, options, new ResultOrigin(text, database, index++), cancellationToken);
                execution.DrainPending();
            }
            while (await reader.NextResultAsync(cancellationToken));
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Severity 17+ errors still throw
            foreach (SqlError error in ex.Errors)
                execution.Enqueue(ToMessage(error, line));
        }

        execution.DrainPending();
    }

    private sealed record ResultOrigin(string BatchSql, string Database, int Index);

    private static async Task ReadResultAsync(SqlDataReader reader, QueryExecution execution, RunOptions options, ResultOrigin origin, CancellationToken cancellationToken)
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

        var set = new ResultSet(columns) { BatchSql = origin.BatchSql, Database = origin.Database, IndexInBatch = origin.Index };
        execution.Results.Add(set);
        var lastProgress = Stopwatch.GetTimestamp();

        while (await reader.ReadAsync(cancellationToken))
        {
            set.TotalRows++;
            if (set.Rows.Count < options.MaxRowsPerResult)
                set.Rows.Add(ReadRow(reader, columns));
            else
                set.Truncated = true;

            if (options.OnProgress is not null && Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds > 300)
            {
                execution.DrainPending();
                await options.OnProgress();
                lastProgress = Stopwatch.GetTimestamp();
            }
        }
    }

    private static string?[] ReadRow(SqlDataReader reader, List<ResultColumn> columns)
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
                // CLR types (geography, geometry, hierarchyid) need Microsoft.SqlServer.Types to be read; show the bytes like SSMS
                try
                {
                    row[i] = SqlValues.Hex(reader.GetSqlBytes(i).Value, 32768);
                }
                catch (Exception ex)
                {
                    row[i] = "<" + ex.Message + ">";
                }
            }
        }

        return row;
    }

    private static QueryMessage ToMessage(SqlError error, int batchLine)
    {
        // Severity 10 and below are informational (PRINT, RAISERROR ... 10, DBCC output)
        if (error.Class <= 10)
            return new QueryMessage(MessageKind.Info, error.Message);

        var inProcedure = !string.IsNullOrEmpty(error.Procedure);
        var line = inProcedure ? error.LineNumber : batchLine + Math.Max(error.LineNumber, 1) - 1;
        var where = inProcedure ? $", Procedure {error.Procedure}" : string.Empty;

        return new QueryMessage(
            MessageKind.Error,
            $"Msg {error.Number}, Level {error.Class}, State {error.State}{where}, Line {line}\n{error.Message}",
            inProcedure ? null : line);
    }
}
