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
///         child that is new is inserted, and a child no longer held is deleted. Every successful save remembers
///         what it wrote the same way, so the row is read once however many times it is saved.
///     </para>
///     <para>
///         Nothing is read again to write it. The <c>Version</c> compared is the one the caller READ, so a save built
///         on a stale copy — or of a row soft-deleted since, which bumps it — is a
///         <see cref="DbUpdateConcurrencyException" />, and so is one whose row (or a changed child's) is gone. An
///         aggregate with no <c>Version</c> that soft-deletes is asked first, by its key alone, whether it is still
///         there.
///     </para>
/// </remarks>
internal static class AggregateSave
{
    // What each aggregate looked like when it was read or last saved: the values of every entity in it, shadow
    // columns included.
    private static readonly ConditionalWeakTable<object, Dictionary<(Type, string), object>> AsRead = new();

    // The aggregates staged on a context, remembered once that context's save succeeds.
    private static readonly ConditionalWeakTable<DbContext, List<object>> Staged = new();

    /// <summary>Remembers <paramref name="aggregate" /> as <paramref name="context" /> tracks it now.</summary>
    internal static void Remember(DbContext context, object aggregate)
    {
        var copies = new Dictionary<(Type, string), object>();

        foreach (var (type, key, entity) in Walk(context.Entry(aggregate).Metadata, aggregate))
        {
            copies[(type.ClrType, key)] = context.Entry(entity).CurrentValues.Clone();
        }

        AsRead.AddOrUpdate(aggregate, copies);
        StoredAggregates.Mark(aggregate);
    }

    /// <summary>
    ///     Remembers what a save of <paramref name="context" /> wrote for each aggregate staged on it — the caller's
    ///     <c>SaveChangesAsync</c> after <c>Save(db)</c> included — so its next save reads nothing.
    /// </summary>
    internal static void RememberSaved(DbContext context)
    {
        if (!Staged.TryGetValue(context, out var staged))
        {
            return;
        }

        Staged.Remove(context);

        foreach (var aggregate in staged)
        {
            if (context.Entry(aggregate).State == EntityState.Unchanged)
            {
                Remember(context, aggregate);
            }
        }
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

        // Already done by the interceptor of a context that has Rask's; this one may not.
        if (!AsRead.TryGetValue(aggregate, out _))
        {
            Remember(context, aggregate);
        }
    }

    /// <summary>Stages the save on <paramref name="context" />, for its owner to commit.</summary>
    internal static async Task StageAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, TEntity aggregate, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        // Before the row is looked for: with no tenant resolved it would only be reported as deleted.
        Tenant.DemandForWrite(typeof(TEntity));

        var entry = context.Entry(aggregate);
        Staged.GetOrCreateValue(context).Add(aggregate);

        // Loaded through this very context: its own change tracking already knows what to write.
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        // A key the database generates and has not yet, or one code made and nothing has read or saved: it was never
        // saved, so there is no row to look for.
        if (!entry.IsKeySet || StoredAggregates.IsNew(context, aggregate))
        {
            context.Add(aggregate);
            return;
        }

        var keyValues = KeyOf(entry.Metadata, aggregate);

        // What was read or last saved is what the save is compared with, and the row is not read again.
        if (AsRead.TryGetValue(aggregate, out var read))
        {
            await RefuseSoftDeletedSinceAsync<TEntity>(context, entry.Metadata, keyValues, cancellationToken)
                .ConfigureAwait(false);
            StageOver(context, entry, aggregate, read, read);
            return;
        }

        // Stored, but nothing remembered of it — staged on a context of the caller's whose save was never seen.
        var stored = await AggregateLoad.FindAsync<TEntity>(context, keyValues, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            if (await AggregateLoad.ExistsIgnoringFiltersAsync<TEntity>(context, keyValues, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw DeletedSinceRead<TEntity>(keyValues);
            }

            context.Add(aggregate);
            return;
        }

        var now = Index(entry.Metadata, stored);
        StageOver(context, entry, aggregate, now, now);
    }

    /// <summary>
    ///     Stages the delete of the aggregate the caller holds — <c>await file.Delete()</c> — from what was read: a stamp
    ///     for one that soft-deletes, the row and its children otherwise, with no read first.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was never saved, so there is no row to delete.</exception>
    /// <exception cref="KeyNotFoundException">Its row is gone (or soft-deleted) and no Version can say so at the commit.</exception>
    internal static async Task StageDeleteAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, TEntity aggregate, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        Tenant.DemandForWrite(typeof(TEntity));

        var entry = context.Entry(aggregate);

        if (entry.State == EntityState.Detached)
        {
            if (!entry.IsKeySet || StoredAggregates.IsNew(context, aggregate))
            {
                throw new InvalidOperationException(
                    $"This {typeof(TEntity).Name} has never been saved, so there is no row to delete. Let it go instead.");
            }

            var keyValues = KeyOf(entry.Metadata, aggregate);

            if (AsRead.TryGetValue(aggregate, out var read))
            {
                await RefuseSoftDeletedSinceAsync<TEntity>(context, entry.Metadata, keyValues, cancellationToken)
                    .ConfigureAwait(false);
                StageOver(context, entry, aggregate, read, read);
            }
            else
            {
                var stored = await AggregateLoad.FindAsync<TEntity>(context, keyValues, cancellationToken).ConfigureAwait(false)
                             ?? throw DeletedSinceRead<TEntity>(keyValues);
                var now = Index(entry.Metadata, stored);
                StageOver(context, entry, aggregate, now, now);
            }
        }

        // The Version stays the one read: a row changed or deleted since is refused at the commit.
        TrackedReferenceRefusal.Remove(context, aggregate);
    }

    // With a Version, a soft delete since the read bumped it and the save is refused at the commit. Without one the
    // UPDATE would land on the deleted row, so the key alone is asked first — not the row, which may be large.
    private static async Task RefuseSoftDeletedSinceAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, IEntityType rootType, object?[] keyValues, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        if (rootType.FindProperty(Columns.DeletedAt) is null || rootType.FindProperty(Columns.Version) is not null)
        {
            return;
        }

        if (!await AggregateLoad.ExistsAsync<TEntity>(context, keyValues, cancellationToken).ConfigureAwait(false))
        {
            throw DeletedSinceRead<TEntity>(keyValues);
        }
    }

    private static KeyNotFoundException DeletedSinceRead<TEntity>(object?[] keyValues) =>
        new($"{typeof(TEntity).Name} '{string.Join(", ", keyValues)}' has been deleted since it was read, " +
            "so there is nothing to save it over.");

    // Attaches the aggregate over the row it replaces, so only what this code changed is written. `now` is every entity
    // the stored aggregate holds — the row as it is, or as last read — and `before` what each looked like when read.
    private static void StageOver(
        DbContext context,
        EntityEntry entry,
        object aggregate,
        Dictionary<(Type, string), object> now,
        Dictionary<(Type, string), object> before)
    {
        var rootType = entry.Metadata;

        // A context the caller commits may never commit, so what it writes is remembered only once it does.
        AsRead.Remove(aggregate);

        var version = rootType.FindProperty(Columns.Version) is { } token
            ? entry.Property(token.Name).CurrentValue
            : null;

        Attach(context, rootType, aggregate);

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
                // A row keyed by its parent's key and its own parts was built anew with a key of a stored row: Attach
                // took the parent part it had not been given yet for a new row, but the key names the stored one.
                if (tracked.State == EntityState.Added)
                {
                    tracked.State = EntityState.Unchanged;
                }

                // Original = as read, so only what this code changed is written.
                Restore(tracked, original);
            }
        }

        foreach (var (key, row) in now)
        {
            // Only what was read and let go: a child somebody added since was never this code's to delete.
            if (!held.Contains(key) && before.TryGetValue(key, out var asRead))
            {
                // Entry, not Remove: Remove would attach the loaded copy's whole graph beside the live one. A child known
                // only by its remembered values is rebuilt from them, to be deleted by its key.
                var removed = row is PropertyValues values ? values.ToObject() : row;
                var gone = context.Entry(removed);
                gone.State = EntityState.Deleted;
                Restore(gone, asRead);
                ChildWrites.LetGoByItsRoot(removed);
            }
        }

        if (version is not null)
        {
            entry.Property(Columns.Version).OriginalValue = version;
        }
    }

    // A column the type does not declare — a PerTenant<int> root's TenantId, a child's foreign key — is not on the
    // object attached, so it is put back as read: the tenant check reads it, and a save must not null it.
    private static void Restore(EntityEntry tracked, object original)
    {
        if (original is not PropertyValues read)
        {
            tracked.OriginalValues.SetValues(original);
            return;
        }

        tracked.OriginalValues.SetValues(read);

        if (tracked.State == EntityState.Deleted)
        {
            return;
        }

        foreach (var shadow in tracked.Metadata.GetProperties()
                     .Where(static p => p.IsShadowProperty() && !p.IsKey() && !p.IsForeignKey()))
        {
            var column = tracked.Property(shadow);
            column.CurrentValue = read[shadow];
            column.IsModified = false;
        }
    }

    // Attaching fixes each child up into its holder's collection, so a read-only one is swapped first.
    private static void Attach(DbContext context, IEntityType rootType, object aggregate)
    {
        foreach (var (type, _, entity) in Walk(rootType, aggregate))
        {
            WritableChildren.Ensure(type, entity);
        }

        context.Attach(aggregate);
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
