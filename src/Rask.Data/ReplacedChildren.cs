using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Matches a child keyed by several columns that an aggregate replaced with a NEW instance carrying the same key —
///     <c>_fuellings.Clear()</c> and refilled — back onto the row the context already tracks, so a save updates what
///     changed instead of failing on two instances of one key.
/// </summary>
/// <remarks>
///     <para>
///         Such a key is the row's identity, not a number the store hands out: a refilled collection names the same
///         rows again. The tracked instance takes the new one's values and its place in the collection, so the save
///         writes an UPDATE of the changed columns, nothing for an unchanged row, an INSERT for a new key and a DELETE
///         for a key no longer held. The new instance's own children move onto it and are matched the same way.
///     </para>
///     <para>
///         Runs before change detection, which is where EF Core would refuse the second instance: with automatic
///         detection off, reading the tracker sees the collections exactly as the aggregate left them.
///     </para>
/// </remarks>
internal static class ReplacedChildren
{
    // The same ceiling AggregateChildren walks down with.
    private const int Deepest = 4;

    // Whether a model holds any child keyed by several columns: a save of one that holds none looks at nothing.
    private static readonly ConditionalWeakTable<IModel, StrongBox<bool>> Models = [];

    // Rask's to keep, never the new instance's: it has no CreatedAt of the row it stands for.
    private static readonly HashSet<string> FrameworkColumns = new(StringComparer.Ordinal)
    {
        Columns.CreatedAt, Columns.UpdatedAt, Columns.DeletedAt, Columns.Version, Columns.TenantId, Columns.TenantKey,
    };

    /// <summary>Puts each tracked row back in place of a new instance of the same key in its aggregate.</summary>
    internal static void Match(DbContext context)
    {
        if (!Models.GetValue(context.Model, static m => new(m.GetEntityTypes().Any(IsKeyedByParts))).Value)
        {
            return;
        }

        var tracker = context.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            var entries = tracker.Entries().ToList();
            var tracked = new Dictionary<(IEntityType, string), EntityEntry>();

            foreach (var entry in entries.Where(static e => IsKeyedByParts(e.Metadata)))
            {
                tracked[(entry.Metadata, KeyOf(entry))] = entry;
            }

            if (tracked.Count == 0)
            {
                return;
            }

            var instances = new HashSet<object>(entries.Select(static e => e.Entity), ReferenceEqualityComparer.Instance);

            foreach (var root in entries.Where(static e => e is { Entity: IAggregate, State: not EntityState.Deleted }))
            {
                MatchHeld(root.Entity, root.Metadata, tracked, instances, depth: 0);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    // Each collection of children `holder` holds, at any depth.
    private static void MatchHeld(
        object holder,
        IEntityType holderType,
        Dictionary<(IEntityType, string), EntityEntry> tracked,
        HashSet<object> instances,
        int depth)
    {
        if (depth >= Deepest)
        {
            return;
        }

        foreach (var navigation in holderType.GetNavigations())
        {
            if (!navigation.IsCollection ||
                !AggregateChildren.IsChild(navigation.TargetEntityType.ClrType) ||
                navigation.GetGetter().GetClrValue(holder) is not IEnumerable collection)
            {
                continue;
            }

            var children = collection.Cast<object>().ToList();
            var held = new HashSet<object>(children, ReferenceEqualityComparer.Instance);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (!instances.Contains(child) &&
                    IsKeyedByParts(navigation.TargetEntityType) &&
                    (navigation.TargetEntityType, KeyUnder(holder, navigation, child)) is var key &&
                    tracked.TryGetValue(key, out var stored) &&
                    !held.Contains(stored.Entity))
                {
                    tracked.Remove(key);
                    TakeOver(navigation, holder, collection, i, child, stored);
                    child = stored.Entity;
                }

                MatchHeld(child, navigation.TargetEntityType, tracked, instances, depth + 1);
            }
        }
    }

    // The tracked row takes the new instance's values, its children and its place.
    private static void TakeOver(
        INavigation navigation, object holder, IEnumerable collection, int index, object fresh, EntityEntry stored)
    {
        var type = stored.Metadata;

        // Seen as dropped by a detection that ran before the refill: it is held again, so it is a row to keep.
        if (stored.State == EntityState.Deleted)
        {
            stored.State = EntityState.Unchanged;
        }

        foreach (var property in type.GetProperties().Where(static p =>
                     !p.IsPrimaryKey() && !p.IsShadowProperty() && !p.IsForeignKey() && !FrameworkColumns.Contains(p.Name)))
        {
            stored.Property(property).CurrentValue = property.GetGetter().GetClrValue(fresh);
        }

        foreach (var complex in type.GetComplexProperties())
        {
            var value = complex.GetGetter().GetClrValue(fresh);
            if (complex.IsCollection)
            {
                stored.ComplexCollection(complex).CurrentValue = (IList?)value;
            }
            else
            {
                stored.ComplexProperty(complex).CurrentValue = value;
            }
        }

        foreach (var children in type.GetNavigations().Where(static n => n.IsCollection))
        {
            var accessor = children.GetCollectionAccessor()!;

            foreach (var old in ((IEnumerable?)children.GetGetter().GetClrValue(stored.Entity))?.Cast<object>().ToList() ?? [])
            {
                accessor.Remove(stored.Entity, old);
            }

            foreach (var moved in ((IEnumerable?)children.GetGetter().GetClrValue(fresh))?.Cast<object>().ToList() ?? [])
            {
                accessor.Add(stored.Entity, moved, forMaterialization: false);
            }
        }

        if (collection is IList { IsReadOnly: false, IsFixedSize: false } list)
        {
            list[index] = stored.Entity;
            return;
        }

        var held = navigation.GetCollectionAccessor()!;
        held.Remove(holder, fresh);
        held.Add(holder, stored.Entity, forMaterialization: false);
    }

    // The new instance's key, its parent's part read from the holder: nothing has fixed its foreign key up yet.
    private static string KeyUnder(object holder, INavigation navigation, object child)
    {
        var foreignKey = navigation.ForeignKey;
        var values = navigation.TargetEntityType.FindPrimaryKey()!.Properties.Select(property =>
        {
            var at = IndexOf(foreignKey.Properties, property);
            return at >= 0
                ? foreignKey.PrincipalKey.Properties[at].GetGetter().GetClrValue(holder)
                : property.GetGetter().GetClrValue(child);
        });

        return Joined(values);
    }

    private static int IndexOf(IReadOnlyList<IProperty> properties, IProperty property)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (ReferenceEquals(properties[i], property))
            {
                return i;
            }
        }

        return -1;
    }

    private static string KeyOf(EntityEntry entry) =>
        Joined(entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p).CurrentValue));

    private static string Joined(IEnumerable<object?> values) =>
        string.Join('\u001f', values.Select(static v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));

    internal static bool IsKeyedByParts(IEntityType type) =>
        AggregateChildren.IsChild(type.ClrType) && ModelBuilderExtensions.HasCompositeKey(type.ClrType);
}
