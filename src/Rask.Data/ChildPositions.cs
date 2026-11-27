using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Keeps the order of a <see cref="HasMany{TChild}" /> its aggregate declared <c>InOrder</c>: each child's place in it
///     is stored in a column of its own (<see cref="Columns.Position" />, a shadow property), written on every save and
///     read back as it loads, so the children come back in the order the aggregate last gave them — a store promises no
///     order of its own.
/// </summary>
internal static class ChildPositions
{
    // Where each child loaded: its stored place, until the collection it is loaded into has taken it in order.
    private static readonly ConditionalWeakTable<object, StrongBox<int>> Loaded = [];

    // Each entity type's InOrder navigations, so a row of a type that holds none costs a lookup and nothing more.
    private static readonly ConditionalWeakTable<IEntityType, INavigation[]> Ordered = [];

    /// <summary>Remembers where a child just materialized was stored, for the collection it is fixed up into.</summary>
    internal static void Read(MaterializationInterceptionData data, object entity)
    {
        if (data.EntityType.FindProperty(Columns.Position) is { } column && column.IsShadowProperty())
        {
            Loaded.AddOrUpdate(entity, new StrongBox<int>(data.GetPropertyValue<int>(Columns.Position)));
        }
    }

    /// <summary>The place <paramref name="child" /> was stored at, once, or null for a child no query just loaded.</summary>
    internal static int? Take(object child) =>
        Loaded.TryGetValue(child, out var position) && Loaded.Remove(child) ? position.Value : null;

    /// <summary>Writes each child's place in its <c>InOrder</c> collection into its position column, before a save.</summary>
    internal static void Stamp(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            foreach (var navigation in Ordered.GetValue(entry.Metadata, static t => [.. t.GetNavigations().Where(static n => n.IsCollection && n.IsInOrder())]))
            {
                if (navigation.GetGetter().GetClrValue(entry.Entity) is not IEnumerable children)
                {
                    continue;
                }

                var position = 0;
                foreach (var child in children)
                {
                    var held = context.Entry(child);
                    if (held.Metadata.FindProperty(Columns.Position) is { } column && column.IsShadowProperty() &&
                        held.State != EntityState.Detached && !Equals(held.Property(Columns.Position).CurrentValue, position))
                    {
                        held.Property(Columns.Position).CurrentValue = position;
                    }

                    position++;
                }
            }
        }
    }
}
