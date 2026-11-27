using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Maps one of the framework's stamps — <c>CreatedAt</c>, <c>UpdatedAt</c>, <c>DeletedAt</c> — as the
///     <see cref="Timestamp" /> column it is: the UTC instant, on every provider.
/// </summary>
/// <remarks>
///     A class that declares the stamp itself with another type keeps it as declared — a <see cref="DateTime" /> written
///     before Rask held its stamps as moments, which RASK147 points at — and a stamp no class declares is a shadow
///     <see cref="Timestamp" />.
/// </remarks>
internal static class TimestampColumn
{
    /// <summary>Maps <paramref name="column" /> on <paramref name="builder" />'s entity.</summary>
    /// <param name="builder">The entity's builder.</param>
    /// <param name="column">The stamp's column, one of <see cref="Columns" />.</param>
    internal static void Map(EntityTypeBuilder builder, string column)
    {
        var type = builder.Metadata.ClrType.GetProperty(column)?.PropertyType ?? typeof(Timestamp);
        var property = builder.Property(type, column);
        if (type == typeof(Timestamp))
        {
            ValueObjectColumn.MapTimestamp(property);
        }
    }

    /// <summary>What a stamp of <paramref name="clrType" /> is set to at <paramref name="utcNow" />.</summary>
    /// <param name="clrType">The stamp's type: <see cref="Timestamp" />, or a <see cref="DateTime" /> a class declared.</param>
    /// <param name="utcNow">Now, in UTC.</param>
    /// <returns>The value to set.</returns>
    internal static object Of(Type clrType, DateTime utcNow) =>
        (Nullable.GetUnderlyingType(clrType) ?? clrType) == typeof(DateTime) ? utcNow : Timestamp.Create(utcNow);
}
