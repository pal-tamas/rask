using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Maps a <see cref="Timestamp" /> of a battery's own table as the UTC instant it holds — on any context, a plain
///     <c>DbContext</c> on a provider Rask does not configure included, where no <c>UtcMomentConvention</c> runs and a
///     moment's column would otherwise keep the wall clock. A battery's times were always written as UTC; so they stay.
/// </summary>
internal static class TimestampColumn
{
    // Two timestamps are the same when they hold the same instant — not the same wall-clock time, which the hour the
    // clock shows twice in autumn gives two of. Immutable, so a snapshot is the value itself.
    private static readonly ValueComparer<Timestamp?> SameInstant = new(
        static (a, b) => a == b,
        static t => t == null ? 0 : t.Instant.GetHashCode(),
        static t => t);

    /// <summary>Stores <paramref name="property" />, a <see cref="Timestamp" />, as the UTC instant it holds.</summary>
    /// <param name="property">The entity's property that holds the timestamp.</param>
    /// <returns>The same builder.</returns>
    public static PropertyBuilder Map(PropertyBuilder property)
    {
        ValueObjectColumn.Map(property, static (Timestamp t) => t.Value);
        var mutable = property.Metadata;
        mutable.SetValueConverter(OneValueConverters.StoringInstant(mutable.GetValueConverter()!));
        mutable.SetValueComparer(SameInstant);
        return property;
    }
}
