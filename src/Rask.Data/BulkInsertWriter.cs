using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
/// The change-tracker-free write path: inside a transaction, either one prepared single-row <c>INSERT</c> rebound
/// per row, or <c>INSERT … VALUES (…),(…)</c> packed as many rows to a statement as the provider allows — chosen by
/// what a round trip costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>SQLite: one prepared row at a time.</b> It wins because of what it does <i>not</i> do. It never materialises an
/// entity entry, so nothing grows with the row count; and it hands the provider one statement to parse for the
/// whole load, so SQLite re-plans nothing. Measured over 100,000 rows it runs in about a quarter of the time of the
/// batched change-tracker path and allocates about an eighth as much. A multi-row statement loses there at every
/// packing: each distinct row count is a new statement to parse, and Microsoft.Data.Sqlite binds parameters by
/// name, so a statement packed to SQLite's 32,766-parameter limit is quadratic in its own parameter count.
/// </para>
/// <para>
/// <b>A server: as many rows per round trip as it takes.</b> On PostgreSQL, SQL Server and MySQL every statement is
/// a round trip, so the per-row shape is paid in latency once per row. Measured against PostgreSQL 17 with 1 ms of
/// added latency (<c>PostgresBulkInsertBenchmarks</c>, #1063, 15 iterations), 10,000 rows took 20.1 s one row at a
/// time, 225 ms through the change tracker, 160 ms as 1,000-command <c>DbBatch</c>es and 120 ms as 1,000-row
/// <c>VALUES</c> lists, which also allocated less than the batches. The packed statement is not explicitly prepared: its parse is small against a
/// round trip, and a server cannot prepare a parameter whose only clue to its type is a null.
/// </para>
/// </remarks>
internal static class BulkInsertWriter
{
    /// <summary>SQL Server's cap on the rows of one <c>VALUES</c> list; used for every server so they share one shape.</summary>
    internal const int MaxRowsPerStatement = 1_000;

    internal static Task<int> WriteAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken)
        where TEntity : class =>
        WriteAsync(
            context,
            entities,
            options,
            RowsPerStatement(context.Database.ProviderName, BulkInsertPlan.For<TEntity>(context).Columns.Count),
            cancellationToken);

    /// <summary>
    /// <see cref="WriteAsync{TEntity}(DbContext, IEnumerable{TEntity}, BulkInsertOptions, CancellationToken)"/> with the
    /// packing stated, so the packed writer can be exercised on SQLite, which never chooses it.
    /// </summary>
    internal static async Task<int> WriteAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        BulkInsertOptions options,
        int rowsPerStatement,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var plan = BulkInsertPlan.For<TEntity>(context);
        var now = ResolveTimeProvider(context).GetUtcNow().UtcDateTime;
        var synchronous = ExecutesSynchronously(context.Database.ProviderName);
        var strategy = context.Database.CreateExecutionStrategy();
        var write = new BatchWrite(plan, now, synchronous, rowsPerStatement);

        var written = 0;
        foreach (var batch in entities.Chunk(options.BatchSize))
        {
            written += context.Database.CurrentTransaction is null
                // No ambient transaction, so each batch commits on its own — which also makes it the unit a
                // retrying strategy replays: a failed batch rolled back whole, and nothing committed before it
                // is repeated. The change-tracker path gets the same from EF around each SaveChanges.
                ? await strategy.ExecuteAsync(
                        (Write: write, Rows: batch),
                        static (ctx, state, token) => WriteBatchAsync(ctx, state.Write, state.Rows, token),
                        verifySucceeded: null,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await WriteBatchAsync(context, write, batch, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>How many rows one statement carries on <paramref name="providerName"/>: 1 means one prepared row at a time.</summary>
    /// <remarks>
    /// Bounded by the provider's parameter limit (SQL Server 2,100 per request, PostgreSQL and MySQL 65,535 per
    /// statement) and by <see cref="MaxRowsPerStatement"/>. A provider not named here keeps the per-row shape, which
    /// is correct everywhere, only slow where there is a network.
    /// </remarks>
    /// <param name="providerName">The context's <c>Database.ProviderName</c>.</param>
    /// <param name="columns">The columns each row binds.</param>
    internal static int RowsPerStatement(string? providerName, int columns) => providerName switch
    {
        "Npgsql.EntityFrameworkCore.PostgreSQL" => Pack(65_535, columns),
        // 2,100 is the request's limit; EF's own batching leaves room for two, and so does this.
        "Microsoft.EntityFrameworkCore.SqlServer" => Pack(2_098, columns),
        "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => Pack(65_535, columns),
        _ => 1,
    };

    private static int Pack(int maxParameters, int columns) => Math.Clamp(maxParameters / columns, 1, MaxRowsPerStatement);

    private static async Task<int> WriteBatchAsync<TEntity>(
        DbContext context,
        BatchWrite write,
        TEntity[] batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        // Opened THROUGH EF, never DbConnection.OpenAsync. EF's connection interceptors fire only on an open EF
        // performs — UseRaskSqlite's pragmas, and anything the app registered — so a raw open ran every row with
        // foreign_keys and busy_timeout at SQLite's defaults. EF also counts opens: a connection the caller (or
        // SingleTransaction) already opened stays open when this closes its own.
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = context.Database.GetDbConnection();

        // An ambient transaction — the caller's, or the one BulkInsertAsync opened for SingleTransaction —
        // owns the commit; otherwise each batch is its own unit, matching the change-tracker path.
        var ambient = context.Database.CurrentTransaction?.GetDbTransaction();
        var owned = ambient is null
            ? await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            var transaction = ambient ?? owned;
            var written = write.RowsPerStatement > 1
                ? await WritePackedAsync(connection, transaction, write, batch, cancellationToken).ConfigureAwait(false)
                : await WriteRowsAsync(connection, transaction, write, batch, cancellationToken).ConfigureAwait(false);

            if (owned is not null)
            {
                await owned.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return written;
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }

            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> WriteRowsAsync<TEntity>(
        DbConnection connection,
        DbTransaction? transaction,
        BatchWrite write,
        TEntity[] batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var plan = write.Plan;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = plan.CommandText;
        var parameters = AddParameters(command, plan, rows: 1, static (p, i) => p.Columns[i].ParameterName);

        var written = 0;
        var prepared = false;
        foreach (var entity in batch)
        {
            Bind(write, entity, parameters, offset: 0);

            // Prepared once the first row is bound, never before: Npgsql cannot prepare a parameter that has
            // neither a type nor a value, and a column whose EF mapping carries no DbType (a decimal, a bool)
            // has only its value to say what it is (#1063).
            if (!prepared)
            {
                if (write.Synchronous)
                {
                    command.Prepare();
                }
                else
                {
                    await command.PrepareAsync(cancellationToken).ConfigureAwait(false);
                }

                prepared = true;
            }

            written += write.Synchronous
                ? command.ExecuteNonQuery()
                : await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    private static async Task<int> WritePackedAsync<TEntity>(
        DbConnection connection,
        DbTransaction? transaction,
        BatchWrite write,
        TEntity[] batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var plan = write.Plan;
        var columns = plan.Columns.Count;

        // One command for every full statement in the batch, rebound each time; the remainder gets its own.
        DbCommand? full = null;
        DbParameter[]? fullParameters = null;
        var written = 0;
        try
        {
            foreach (var rows in batch.Chunk(write.RowsPerStatement))
            {
                DbCommand command;
                DbParameter[] parameters;
                if (rows.Length == write.RowsPerStatement)
                {
                    full ??= CreatePacked(connection, transaction, plan, rows.Length, out fullParameters);
                    command = full;
                    parameters = fullParameters!;
                }
                else
                {
                    command = CreatePacked(connection, transaction, plan, rows.Length, out parameters);
                }

                try
                {
                    for (var r = 0; r < rows.Length; r++)
                    {
                        Bind(write, rows[r], parameters, offset: r * columns);
                    }

                    written += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (!ReferenceEquals(command, full))
                    {
                        await command.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            return written;
        }
        finally
        {
            if (full is not null)
            {
                await full.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static DbCommand CreatePacked(
        DbConnection connection, DbTransaction? transaction, BulkInsertPlan plan, int rows, out DbParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = plan.PackedCommandText(rows);
        parameters = AddParameters(command, plan, rows, static (p, i) => p.PackedParameterName(i));
        return command;
    }

    private static DbParameter[] AddParameters(DbCommand command, BulkInsertPlan plan, int rows, Func<BulkInsertPlan, int, string> name)
    {
        var columns = plan.Columns.Count;
        var parameters = new DbParameter[rows * columns];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name(plan, i);
            if (plan.Columns[i % columns].DbType is { } dbType)
            {
                parameter.DbType = dbType;
            }

            command.Parameters.Add(parameter);
            parameters[i] = parameter;
        }

        return parameters;
    }

    private static void Bind(BatchWrite write, object entity, DbParameter[] parameters, int offset)
    {
        var plan = write.Plan;

        // AuditingInterceptor never sees these rows, so the writer stamps them itself — on the entity as well as in
        // the row, so an object the caller keeps reads back what was stored.
        plan.Timestamps?.SetCreatedAt(entity, write.Now);
        plan.Timestamps?.SetUpdatedAt(entity, write.Now);

        for (var c = 0; c < plan.Columns.Count; c++)
        {
            parameters[offset + c].Value = plan.Columns[c].ValueFor(entity) ?? DBNull.Value;
        }
    }

    /// <summary>Whether each row runs through the synchronous <c>ExecuteNonQuery</c>.</summary>
    /// <remarks>
    /// SQLite is a local file with no true async I/O — <c>ExecuteNonQueryAsync</c> runs the same synchronous
    /// work on the calling thread — so the sync call is the honest one per row, and the cheaper. A
    /// client-server provider does a network round trip per statement, where blocking would park a thread for every
    /// one of them.
    /// </remarks>
    /// <param name="providerName">The context's <c>Database.ProviderName</c>.</param>
    internal static bool ExecutesSynchronously(string? providerName) =>
        string.Equals(providerName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    // AuditingInterceptor takes its TimeProvider from DI; the writer must read the same clock or a test that
    // freezes time would see two different "now"s depending on which path ran.
    private static TimeProvider ResolveTimeProvider(DbContext context) =>
        context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider?
            .GetService(typeof(TimeProvider)) as TimeProvider
        ?? Clock.TimeProvider;

    /// <summary>What every batch of one load shares.</summary>
    private sealed record BatchWrite(BulkInsertPlan Plan, DateTime Now, bool Synchronous, int RowsPerStatement);
}
