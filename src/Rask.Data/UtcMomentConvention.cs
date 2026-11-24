using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Stores every <see cref="Moment" /> value object as the UTC instant it is (<see cref="WallClockInUtc" />), for a
///     provider that keeps a <see cref="DateTime" /> only as UTC — PostgreSQL's <c>timestamptz</c>, which refuses a
///     wall-clock time outright. The value object still holds its wall-clock time; only the column holds UTC, so the
///     same moment is saved, read, compared and ordered alike on every database.
/// </summary>
/// <remarks>
///     Runs first among the finalizing conventions, before EF Core picks each column's type mapping from its converter.
///     A converter the app gave the property is composed with it, as Rask's own is; a bare <see cref="DateTime" /> is left
///     as the provider maps it.
/// </remarks>
internal sealed class UtcMomentConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            Store(entityType);
        }
    }

    private static void Store(IConventionTypeBase type)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            var held = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (typeof(Moment).IsAssignableFrom(held) &&
                property.GetValueConverter() is { ProviderClrType: var stored } converter &&
                stored == typeof(DateTime) &&
                OneValueConverters.StoredAs(converter) is null)
            {
                ((IMutableProperty)property).SetValueConverter(OneValueConverters.Then(converter, WallClockInUtc.Converter));
            }
        }

        foreach (var complex in type.GetDeclaredComplexProperties())
        {
            Store(complex.ComplexType);
        }
    }
}
