using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     One <c>b.Projects&lt;E&gt;(watched, raise)</c>: which members of a row it watches, and how it makes its event from
///     the rows a save changed in them.
/// </summary>
internal abstract class DeclaredProjection(Type eventType, string[] watched)
{
    /// <summary>The event it raises; a second declaration of the same event on the same row replaces the first.</summary>
    internal Type EventType { get; } = eventType;

    /// <summary>
    ///     Whether this save changes <paramref name="entry" /> as the projection sees it: added, removed, or a watched member
    ///     holding another value.
    /// </summary>
    internal bool Sees(EntityEntry entry) => entry.State switch
    {
        EntityState.Added or EntityState.Deleted => true,
        EntityState.Modified => watched.Any(name => Changed(entry, name)),
        _ => false,
    };

    /// <summary>The event made from <paramref name="entries" /> — every one of them <see cref="Sees" /> — or null for none.</summary>
    internal abstract IEvent? Raise(List<EntityEntry> entries);

    private static bool Changed(EntityEntry entry, string name)
    {
        if (entry.Metadata.FindProperty(name) is { } property)
        {
            var value = entry.Property(property);
            return value.IsModified && !property.GetValueComparer().Equals(value.OriginalValue, value.CurrentValue);
        }

        // A value object held as a complex property, or a reference whose key moved: EF Core's own answer.
        return entry.Metadata.FindComplexProperty(name) is not null
            ? entry.ComplexProperty(name).IsModified
            : entry.Member(name).IsModified;
    }
}
