using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Saving an aggregate the caller holds — <c>await order.Save()</c> — whether it came from
///     <c>Order.Find(id)</c> or was just built.
/// </summary>
/// <remarks>
///     <para>
///         The aggregate is not tracked between <c>Find</c> and <c>Save</c>: no context is held open while a
///         page or a handler works on it. <c>Find</c> remembers the values it read instead, beside the
///         aggregate and for exactly as long as it lives, and the save writes only what differs from them —
///         so a column somebody else changed meanwhile, and this code did not touch, keeps their value. A
///         child that is new is inserted, and a child no longer held is deleted.
///     </para>
///     <para>
///         The row is loaded again through the query filters, so one soft-deleted since it was read is refused
///         rather than silently written. The <c>Version</c> compared is the one the caller READ, so a save built
///         on a stale copy is a <see cref="DbUpdateConcurrencyException" />.
///     </para>
/// </remarks>
internal static class AggregateSave
{
    // What each aggregate looked like when it was read or last saved: a detached copy of every entity in it.
    private static readonly ConditionalWeakTable<object, Dictionary<(Type, string), object>> AsRead = new();

    /// <summary>Remembers <paramref name="aggregate" /> as <paramref name="context" /> tracks it now.</summary>
    internal static void Remember(DbContext context, object aggregate)
    {
        var copies = new Dictionary<(Type, string), object>();

        foreach (var (type, key, entity) in Walk(context.Entry(aggregate).Metadata, aggregate))
        {
            copies[(type.ClrType, key)] = context.Entry(entity).CurrentValues.ToObject();
        }

        AsRead.AddOrUpdate(aggregate, copies);
    }

    /// <summary>Saves through a context of its own, and remembers what was written for the next save.</summary>
    internal static async Task SaveAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        TEntity aggregate, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);

        await StageAsync(context, aggregate, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Remember(context, aggregate);
    }

    /// <summary>Stages the save on <paramref name="context" />, for its owner to commit.</summary>
    internal static async Task StageAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, TEntity aggregate, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        // Before the row is looked for: with no tenant resolved it would only be reported as deleted.
        Tenant.DemandForWrite(typeof(TEntity));

        var entry = context.Entry(aggregate);

        // Loaded through this very context: its own change tracking already knows what to write.
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        // A key the database generates and has not yet: it was never saved.
        if (!entry.IsKeySet)
        {
            context.Add(aggregate);
            return;
        }

        var keyValues = KeyOf(entry.Metadata, aggregate);
        var stored = await AggregateLoad.FindAsync<TEntity>(context, keyValues, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            if (await AggregateLoad.ExistsIgnoringFiltersAsync<TEntity>(context, keyValues, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new KeyNotFoundException(
                    $"{typeof(TEntity).Name} '{string.Join(", ", keyValues)}' has been deleted since it was read, " +
                    "so there is nothing to save it over.");
            }

            context.Add(aggregate);
            return;
        }

        StageOver(context, entry, aggregate, stored);
    }

    // Attaches the aggregate over the stored row it replaces, so only what this code changed is written.
    private static void StageOver(DbContext context, EntityEntry entry, object aggregate, object stored)
    {
        // Compared with what was READ when that is known; with the row as it is now otherwise (built by hand
        // with an existing key, or last saved through a context the caller committed).
        var rootType = entry.Metadata;
        var now = Index(rootType, stored);
        var before = AsRead.TryGetValue(aggregate, out var read) ? read : now;

        // A context the caller commits may never commit, so what it writes is not remembered.
        AsRead.Remove(aggregate);

        var version = rootType.FindProperty(Columns.Version) is { } token
            ? entry.Property(token.Name).CurrentValue
            : null;

        context.Attach(aggregate);

        var held = new HashSet<(Type, string)>();

        foreach (var (type, key, entity) in Walk(rootType, aggregate))
        {
            held.Add((type.ClrType, key));
            var tracked = context.Entry(entity);

            if (!now.ContainsKey((type.ClrType, key)))
            {
                // Its key was assigned in its constructor, so Attach took it for an existing row.
                tracked.State = EntityState.Added;
            }
            else if (before.TryGetValue((type.ClrType, key), out var original))
            {
                // Original = as read, so only what this code changed is written.
                tracked.OriginalValues.SetValues(original);
            }
        }

        foreach (var (key, row) in now)
        {
            // Only what was read and let go: a child somebody added since was never this code's to delete.
            if (!held.Contains(key) && before.ContainsKey(key))
            {
                // Entry, not Remove: Remove would attach the loaded copy's whole graph beside the live one.
                context.Entry(row).State = EntityState.Deleted;
            }
        }

        if (version is not null)
        {
            entry.Property(Columns.Version).OriginalValue = version;
        }
    }

    private static Dictionary<(Type, string), object> Index(IEntityType rootType, object root)
    {
        var index = new Dictionary<(Type, string), object>();

        foreach (var (type, key, entity) in Walk(rootType, root))
        {
            index[(type.ClrType, key)] = entity;
        }

        return index;
    }

    // The root and every child entity it holds, at any depth — not value collections, which are columns.
    private static IEnumerable<(IEntityType Type, string Key, object Entity)> Walk(IEntityType rootType, object root)
    {
        var pending = new Stack<(IEntityType, object)>();
        pending.Push((rootType, root));

        while (pending.Count > 0)
        {
            var (type, entity) = pending.Pop();

            yield return (type, string.Join('\u001f', KeyOf(type, entity)), entity);

            foreach (var navigation in type.GetNavigations())
            {
                var target = navigation.TargetEntityType;

                if (target.IsOwned() || !AggregateChildren.IsChild(target.ClrType))
                {
                    continue;
                }

                switch (navigation.GetGetter().GetClrValue(entity))
                {
                    case IEnumerable children when navigation.IsCollection:
                        foreach (var child in children)
                        {
                            pending.Push((target, child));
                        }

                        break;
                    case { } child:
                        pending.Push((target, child));
                        break;
                }
            }
        }
    }

    private static object?[] KeyOf(IEntityType type, object entity) =>
        [.. type.FindPrimaryKey()!.Properties.Select(p => p.GetGetter().GetClrValue(entity))];
}
