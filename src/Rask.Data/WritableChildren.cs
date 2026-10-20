using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Swaps a fixed-size collection of children — <c>IReadOnlyList&lt;Line&gt; Lines { get; private set; } = []</c>
///     — for one EF Core can add to, holding the same children.
/// </summary>
/// <remarks>
///     <para>
///         <c>[]</c>, <c>Array.Empty</c> and an array are valid ways to declare an empty collection, and a refill
///         written <c>Lines = [.. rows]</c> leaves a read-only one behind. EF Core writes into whatever the navigation
///         holds and only creates a collection when it holds none, so loading a row into such a collection threw
///         <see cref="NotSupportedException" />. The collection put in its place is the one EF Core would have created.
///     </para>
/// </remarks>
internal static class WritableChildren
{
    // Each entity type's collection navigations, so a row of a type that holds none costs a lookup and nothing more.
    private static readonly ConditionalWeakTable<IEntityType, INavigation[]> Collections = [];

    /// <summary>Does it for every entity <paramref name="context" /> tracks, before a save detects what changed.</summary>
    internal static void Ensure(DbContext context)
    {
        var tracker = context.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var entry in tracker.Entries())
            {
                Ensure(entry.Metadata, entry.Entity);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    /// <summary>Gives every collection navigation of <paramref name="entity" /> a collection EF Core can write to.</summary>
    internal static void Ensure(IEntityType type, object entity)
    {
        foreach (var navigation in Collections.GetValue(type, static t => [.. t.GetNavigations().Where(static n => n.IsCollection)]))
        {
            if (navigation.GetGetter().GetClrValue(entity) is IList held &&
                (held.IsReadOnly || held.IsFixedSize))
            {
                Replace(navigation, entity, held);
            }
        }
    }

    private static void Replace(INavigation navigation, object entity, IList held)
    {
        var accessor = navigation.GetCollectionAccessor()!;
        var writable = accessor.Create();

        foreach (var child in held)
        {
            accessor.AddStandalone(writable, child);
        }

        if (navigation.FieldInfo is { } field)
        {
            field.SetValue(entity, writable);
        }
        else if (navigation.PropertyInfo is { CanWrite: true } property)
        {
            property.SetValue(entity, writable);
        }
    }
}
