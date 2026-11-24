using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     <c>X.UpdateEach</c>: every aggregate a predicate matches loaded, changed through its own method and saved — its
///     rules, events and notices as for one — a batch to a transaction.
/// </summary>
/// <remarks>
///     <para>
///         The keys are read first, so the rows are the ones that matched when it started, in key order; one deleted
///         since is passed over. Each batch is a <c>Db.Transaction</c> of its own, saved in one go, and states why it
///         writes many aggregates: each is valid on its own, so the batch is only a unit of speed, and
///         <see cref="RaskDataOptions.OneAggregatePerTransaction" /> lets it through. A refused batch rolls back alone —
///         the ones before it stay committed, which is why the change it makes should be one that can run twice.
///     </para>
///     <para>
///         Inside a <c>Db.Transaction</c> already, it joins that one, as a nested block does, and its reason then covers
///         the whole of it.
///     </para>
/// </remarks>
internal static class BulkEach
{
    /// <summary>The most rows one batch may hold.</summary>
    internal const int MaxBatchSize = 10_000;

    internal static async Task<int> Update<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(
        Expression<Func<T, bool>> where, Func<T, Task> apply, int batchSize, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(apply);
        CheckBatchSize(batchSize, nameof(batchSize));
        cancellationToken = Ambient.Or(cancellationToken);
        Tenant.DemandForWrite(typeof(T));

        List<object> keys;
        var context = Db.CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var key = BulkKeys.Of<T>(context, "UpdateEach");
            keys = await BulkKeys.Read(context.Set<T>().Where(where), key).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var reason = $"{typeof(T).Name}.UpdateEach: each {typeof(T).Name} is valid on its own, written {batchSize} to a transaction";
        var updated = 0;
        foreach (var batch in keys.Chunk(batchSize))
        {
            updated += await Db.Transaction(() => Batch(batch, apply, cancellationToken), reason, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return updated;
    }

    /// <summary>Refuses a batch size outside 1 to <see cref="MaxBatchSize" />.</summary>
    internal static void CheckBatchSize(int batchSize, string parameter)
    {
        if (batchSize is < 1 or > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(parameter, batchSize, $"The batch size must be between 1 and {MaxBatchSize}.");
        }
    }

    // Each loaded alone (its children with it), staged on one context, and saved once: one write for the batch.
    private static async Task<int> Batch<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(
        object[] keys, Func<T, Task> apply, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);

        var updated = 0;
        foreach (var key in keys)
        {
            if (await GeneratedModelWrites.Find<T>(key, cancellationToken).ConfigureAwait(false) is not { } aggregate)
            {
                continue;
            }

            await apply(aggregate).ConfigureAwait(false);
            await GeneratedModelWrites.Save(aggregate, context, cancellationToken).ConfigureAwait(false);
            updated++;
        }

        await StaleWrite.SaveAsync(context, cancellationToken).ConfigureAwait(false);
        return updated;
    }
}
