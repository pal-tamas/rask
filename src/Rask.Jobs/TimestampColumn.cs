using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;
using Rask.Values;

namespace Rask.Background;

/// <summary>
///     Maps a <see cref="Timestamp" /> property of a jobs table as one column holding the UTC instant — on any context, a
///     plain <c>DbContext</c> as well as a Rask one, whose <c>UtcMomentConvention</c> would store it so too.
/// </summary>
/// <remarks>
///     Stated here rather than left to the convention: a context that is not a Rask one runs no convention, and would keep
///     the wall clock of <see cref="Clock.Zone" /> — the hour the clock shows twice in autumn read back as its first.
/// </remarks>
internal static class TimestampColumn
{
    /// <summary>Stores <paramref name="property" /> as the UTC instant its timestamp holds.</summary>
    /// <param name="property">The timestamp property.</param>
    public static void Map(PropertyBuilder property)
    {
        ValueObjectColumn.Map(property, static (Timestamp t) => t.Value);
        property.Metadata.SetValueConverter(OneValueConverters.StoringInstant(property.Metadata.GetValueConverter()!));
    }
}
