using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>
///     Which aggregates have a row — see <see cref="IStoredAggregate" /> — so a first save of one only code has held
///     is an INSERT with nothing read before it.
/// </summary>
internal static class StoredAggregates
{
    /// <summary>Records that <paramref name="entity" />, when it is an aggregate, has or is being given a row.</summary>
    internal static void Mark(object entity)
    {
        if (entity is IStoredAggregate aggregate)
        {
            aggregate.MarkStored();
        }
    }

    /// <summary>Marks every aggregate <paramref name="context" /> tracks, as a save begins.</summary>
    /// <remarks>
    ///     Before the save rather than after it: a mark only makes the next save read first, so one the database then
    ///     refuses costs a read, never a lost update.
    /// </remarks>
    internal static void MarkTracked(DbContext context)
    {
        var tracker = context.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var entry in tracker.Entries())
            {
                Mark(entry.Entity);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    /// <summary>
    ///     Whether <paramref name="aggregate" /> was made by code and never stored. Answered only through a context that
    ///     would have marked a row it read; through any other, nothing is known to be new.
    /// </summary>
    internal static bool IsNew(DbContext context, object aggregate) =>
        aggregate is IStoredAggregate { IsStored: false } &&
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?
            .Contains(WritableChildrenInterceptor.Instance) == true;
}
