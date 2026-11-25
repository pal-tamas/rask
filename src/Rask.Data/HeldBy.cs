using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The parent keys a changed row is held by, read off the ROW rather than its type: a child two parents may list (a
///     Chip under a Driver or a Vehicle) holds one key and NULL in the other, so the first key its type declares names
///     no parent at all for half its rows.
/// </summary>
/// <param name="Key">The foreign key to the parent.</param>
/// <param name="Values">The row's values of it, none of them null.</param>
internal readonly record struct HeldBy(IForeignKey Key, object?[] Values)
{
    /// <summary>
    ///     The keys among <paramref name="keys" /> that <paramref name="entry" /> has set — as it was read, for a deleted
    ///     row — or, with none set, those it had set when it was read: a row just taken out of its parent's list.
    ///     Several only for a row genuinely held twice; none for a row no parent holds.
    /// </summary>
    /// <param name="entry">The changed row.</param>
    /// <param name="keys">Its type's parent keys, in the order the type declares them.</param>
    /// <returns>Each key the row is held by, with its values.</returns>
    internal static List<HeldBy> Of(EntityEntry entry, IReadOnlyList<IForeignKey> keys)
    {
        var held = Set(entry, keys, entry.State == EntityState.Deleted);
        return held.Count > 0 || entry.State == EntityState.Deleted ? held : Set(entry, keys, original: true);
    }

    private static List<HeldBy> Set(EntityEntry entry, IReadOnlyList<IForeignKey> keys, bool original)
    {
        List<HeldBy> held = [];
        foreach (var key in keys)
        {
            if (ValuesOf(entry, key, original) is { } values)
            {
                held.Add(new HeldBy(key, values));
            }
        }

        return held;
    }

    private static object?[]? ValuesOf(EntityEntry entry, IForeignKey key, bool original)
    {
        var values = new object?[key.Properties.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var property = entry.Property(key.Properties[i].Name);
            values[i] = original ? property.OriginalValue : property.CurrentValue;
            if (values[i] is null)
            {
                return null;
            }
        }

        return values;
    }
}
