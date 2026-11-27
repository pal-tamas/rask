using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;
using Rask.Values;

namespace Rask.Auth;

/// <summary>Maps one of Rask.Auth's <see cref="Timestamp" /> columns.</summary>
internal static class TimestampColumn
{
    /// <summary>
    ///     Stores <paramref name="property" /> as the UTC instant its timestamp holds, as the column always held it, on
    ///     every context and not only one whose provider adds <c>UtcMomentConvention</c>: a plain <c>DbContext</c> on
    ///     <c>UseSqlite</c> would otherwise store the wall clock, and read the hour October shows twice back as its first.
    ///     Without a mapping EF Core would take a <see cref="Timestamp" /> for an entity of its own.
    /// </summary>
    /// <param name="property">The timestamp's property.</param>
    internal static void Map(PropertyBuilder property)
    {
        ValueObjectColumn.Map(property, static (Timestamp t) => t.Value);
        property.Metadata.SetValueConverter(OneValueConverters.StoringInstant(property.Metadata.GetValueConverter()!));
    }
}
