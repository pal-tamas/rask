using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
/// The change-tracker-free write path: one prepared single-row <c>INSERT</c> whose parameters are rebound per
/// row, inside a transaction.
/// </summary>
/// <remarks>
/// This shape wins because of what it does <i>not</i> do. It never materialises an entity entry, so nothing
/// grows with the row count; and it hands the provider one statement to parse for the whole load, so SQLite
/// re-plans nothing. Measured over 100,000 rows it runs in about a quarter of the time of the batched change-
/// tracker path and allocates about an eighth as much. The tempting alternative — a multi-row
/// <c>INSERT … VALUES (…),(…)</c> — loses at every packing: each distinct row count is a new statement to
/// parse, and Microsoft.Data.Sqlite binds parameters by name, so a statement packed to SQLite's
/// 32,766-parameter limit is quadratic in its own parameter count.
/// </remarks>
internal static class BulkInsertWriter
{
    internal static async Task<int> WriteAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var plan = BulkInsertPlan.For<TEntity>(context);
        var now = ResolveTimeProvider(context).GetUtcNow().UtcDateTime;
        var synchronous = ExecutesSynchronously(context.Database.ProviderName);
        var strategy = context.Database.CreateExecutionStrategy();

        var written = 0;
        foreach (var batch in entities.Chunk(options.BatchSize))
        {
            written += context.Database.CurrentTransaction is null
                // No ambient transaction, so each batch commits on its own — which also makes it the unit a
                // retrying strategy replays: a failed batch rolled back whole, and nothing committed before it
                // is repeated. The change-tracker path gets the same from EF around each SaveChanges.
                ? await strategy.ExecuteAsync(
                        batch,
                        (ctx, rows, token) => WriteBatchAsync(ctx, plan, rows, now, synchronous, token),
                        verifySucceeded: null,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await WriteBatchAsync(context, plan, batch, now, synchronous, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    private static async Task<int> WriteBatchAsync<TEntity>(
        DbContext context,
        BulkInsertPlan plan,
        TEntity[] batch,
        DateTime now,
        bool synchronous,
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
            await using var command = connection.CreateCommand();
            command.Transaction = ambient ?? owned;
            command.CommandText = plan.CommandText;

            var parameters = new DbParameter[plan.Columns.Count];
            for (var c = 0; c < plan.Columns.Count; c++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = plan.Columns[c].ParameterName;
                if (plan.Columns[c].DbType is { } dbType)
                {
                    parameter.DbType = dbType;
                }

                command.Parameters.Add(parameter);
                parameters[c] = parameter;
            }

            if (synchronous)
            {
                command.Prepare();
            }
            else
            {
                await command.PrepareAsync(cancellationToken).ConfigureAwait(false);
            }

            var written = 0;
            foreach (var entity in batch)
            {
                // AuditingInterceptor never sees these rows, so the writer stamps them itself — on the
                // entity as well as in the row, so an object the caller keeps reads back what was stored.
                plan.Timestamps?.SetCreatedAt(entity, now);
                plan.Timestamps?.SetUpdatedAt(entity, now);

                for (var c = 0; c < parameters.Length; c++)
                {
                    parameters[c].Value = plan.Columns[c].ValueFor(entity) ?? DBNull.Value;
                }

                written += synchronous
                    ? command.ExecuteNonQuery()
                    : await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

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

    /// <summary>Whether each row runs through the synchronous <c>ExecuteNonQuery</c>.</summary>
    /// <remarks>
    /// SQLite is a local file with no true async I/O — <c>ExecuteNonQueryAsync</c> runs the same synchronous
    /// work on the calling thread — so the sync call is the honest one per row, and the cheaper. A
    /// client-server provider does a network round trip per row, where blocking would park a thread for every
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
        ?? TimeProvider.System;
}
