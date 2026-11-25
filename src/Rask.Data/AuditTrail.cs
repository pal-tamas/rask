using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Rask.Data;

/// <summary>
///     The <see cref="AuditEntry" /> — or the app's own row (<see cref="AuditFormat{TRow, TUserId}" />) — each
///     <see cref="Audited" /> aggregate a save writes leaves, added to that same save: one per aggregate, its children's
///     rows within it under their place (<c>Lines[1].Quantity</c>).
/// </summary>
/// <remarks>
///     <para>
///         Read from what the context tracks as the save starts, after every other step stamped, versioned or rewrote it:
///         an insert records each value it stores, a delete each it held, an update the ones that changed — a value that
///         became null or stopped being null among them. Values are compared and recorded as the database stores them.
///     </para>
///     <para>
///         A key the database generates is not known until the rows went out: an entry that recorded one is completed
///         once they did, before the save's transaction commits (<see cref="SaveFollowUps" />).
///     </para>
/// </remarks>
internal static class AuditTrail
{
    // The entries a save added, taken back out of the context once it is written.
    private static readonly ConditionalWeakTable<DbContext, List<EntityEntry>> Added = [];

    /// <summary>Adds to the save the row of each <see cref="Audited" /> aggregate it writes.</summary>
    /// <param name="context">The context saving.</param>
    /// <param name="registered">The <see cref="AuditFormat" /> the app registered, or Rask's.</param>
    /// <param name="at">When the save is made, in UTC.</param>
    internal static void Record(DbContext context, AuditFormat registered, DateTime at)
    {
        if (AuditEntries.FormatFor(context.Model, registered) is not { } format)
        {
            return;
        }

        // A row is written, never read back through the context: what an earlier save left in it — written, or added
        // by a save that failed — is not this save's.
        foreach (var left in context.ChangeTracker.Entries().Where(e => e.Metadata.ClrType == format.RowType).ToList())
        {
            left.State = EntityState.Detached;
        }

        List<Aggregate> aggregates;
        using (TrackedPrincipals.During(context))
        {
            aggregates = Written(context, format.ReadsSnapshot);
        }

        if (aggregates.Count == 0)
        {
            return;
        }

        var user = format.User();
        var added = new List<EntityEntry>(aggregates.Count);
        foreach (var (root, rows, held) in aggregates)
        {
            var aggregate = root.Metadata.ClrType;
            var draft = AuditDraft.Of(context, root, rows, held);
            var entry = context.Add(format.RowOf(aggregate, draft, user, at));
            added.Add(entry);
            if (draft.HasTemporary)
            {
                // The values the database generated are read once the rows went out, and the row is written again with them.
                SaveFollowUps.Add(context, cancellationToken =>
                {
                    if (entry.State == EntityState.Detached)
                    {
                        return Task.CompletedTask;
                    }

                    draft.ReadTemporary();
                    return AuditRowRewrite.Write(context, entry, format.RowOf(aggregate, draft, user, at), cancellationToken);
                });
            }
        }

        Added.AddOrUpdate(context, added);
    }

    /// <summary>The save went out: its entries are written, and the context lets go of them.</summary>
    internal static void Saved(DbContext? context)
    {
        if (context is null || !Added.TryGetValue(context, out var added))
        {
            return;
        }

        Added.Remove(context);
        foreach (var entry in added)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    ///     The save failed or never started. Its entries stay until the next save takes them out: EF Core is still putting
    ///     back the values a failed save generated, and an entry let go of now would come back into its identity map.
    /// </summary>
    internal static void Failed(DbContext? context)
    {
        if (context is not null)
        {
            Added.Remove(context);
        }
    }

    // Each written row of an Audited aggregate, under its root — and, for a snapshot, every row of it tracked, the root
    // first. Keyed by the root entity: the tracker hands out a new EntityEntry each time it is asked.
    private static List<Aggregate> Written(DbContext context, bool snapshot)
    {
        var aggregates = new Dictionary<object, Aggregate>(ReferenceEqualityComparer.Instance);
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            var written = entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
            if ((!written && !(snapshot && entry.State == EntityState.Unchanged)) || entry.Entity is not IEntity)
            {
                continue;
            }

            var root = entry.Entity is IAggregate ? entry : AuditingInterceptor.RootOf(context, entry);
            if (root is null || !ConventionRegistry.Has(root.Metadata.ClrType, TraitSet.Audited))
            {
                continue;
            }

            if (!aggregates.TryGetValue(root.Entity, out var aggregate))
            {
                aggregate = new Aggregate(root, [], snapshot ? [] : null);
                aggregates.Add(root.Entity, aggregate);
            }

            if (written)
            {
                aggregate.Rows.Add(entry);
            }

            aggregate.Held?.Insert(ReferenceEquals(entry.Entity, root.Entity) ? 0 : aggregate.Held.Count, entry);
        }

        // An aggregate only read in the context is no save's.
        return [.. aggregates.Values.Where(static a => a.Rows.Count > 0)];
    }

    /// <summary>One aggregate a save writes: its root, its rows written, and every row of it tracked when a snapshot is read.</summary>
    private sealed record Aggregate(EntityEntry Root, List<EntityEntry> Rows, List<EntityEntry>? Held);
}
