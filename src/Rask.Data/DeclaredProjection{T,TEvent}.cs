using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>A projection of <typeparamref name="T" /> rows raising <typeparamref name="TEvent" />.</summary>
/// <typeparam name="T">The rows it watches.</typeparam>
/// <typeparam name="TEvent">The event it raises.</typeparam>
internal sealed class DeclaredProjection<T, TEvent>(string[] watched, Func<ProjectedRows<T>, TEvent?> raise)
    : DeclaredProjection(typeof(TEvent), watched)
    where T : class
    where TEvent : class, IEvent
{
    internal override IEvent? Raise(List<EntityEntry> entries)
    {
        var rows = new List<ProjectedRow<T>>(entries.Count);
        foreach (var entry in entries)
        {
            rows.Add(entry.State switch
            {
                EntityState.Added => new ProjectedRow<T>(null, (T)entry.Entity),
                EntityState.Deleted => new ProjectedRow<T>(Stored(entry), null),
                _ => new ProjectedRow<T>(Stored(entry), (T)entry.Entity),
            });
        }

        return raise(new ProjectedRows<T>(rows));
    }

    // A detached copy of the row holding what the database holds now — its own columns, not its children.
    private static T Stored(EntityEntry entry) => (T)entry.OriginalValues.ToObject();
}
