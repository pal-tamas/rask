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
    /// <summary>
    ///     A stamp nothing wrote yet: <c>0001-01-01T00:00:00Z</c>, what an unstamped <c>DateTime</c> stamp held before —
    ///     a context without Rask's interceptors still saves a row, as it always did, rather than a NULL its column refuses.
    /// </summary>
    internal static readonly Timestamp Unset = Timestamp.Create(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));

    /// <summary>Whether <paramref name="stamp" /> was never written: null, or <see cref="Unset" />'s instant.</summary>
    /// <param name="stamp">The stamp.</param>
    /// <returns>True when nothing stamped it.</returns>
    internal static bool IsUnset(Timestamp? stamp) => stamp is null || stamp.Instant.UtcTicks == 0;

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
