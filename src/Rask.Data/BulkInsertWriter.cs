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

        var connection = context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            var written = 0;
            foreach (var batch in entities.Chunk(options.BatchSize))
            {
                written += await WriteBatchAsync(context, connection, plan, batch, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            return written;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> WriteBatchAsync<TEntity>(
        DbContext context,
        DbConnection connection,
        BulkInsertPlan plan,
        TEntity[] batch,
        DateTime now,
        CancellationToken cancellationToken)
        where TEntity : class
    {
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

            command.Prepare();

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

                // SQLite is a local file with no true async I/O — ExecuteNonQueryAsync runs the same
                // synchronous work on the calling thread — so the sync call is the honest one per row.
                written += command.ExecuteNonQuery();
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
        }
    }

    // AuditingInterceptor takes its TimeProvider from DI; the writer must read the same clock or a test that
    // freezes time would see two different "now"s depending on which path ran.
    private static TimeProvider ResolveTimeProvider(DbContext context) =>
        context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider?
            .GetService(typeof(TimeProvider)) as TimeProvider
        ?? TimeProvider.System;
}
