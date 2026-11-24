using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.SQLite;

/// <summary>
///     Holds a <see cref="decimal" /> column of a <c>UseRaskSqlite</c> model to the precision and scale it declares, as
///     SQL Server's <c>decimal(p, s)</c> and PostgreSQL's <c>numeric(p, s)</c> hold it: rounded half away from zero to
///     its scale as it is written, and refused when it has more whole digits than the precision leaves room for.
/// </summary>
/// <remarks>
///     <para>
///         SQLite has no decimal type: EF Core's provider stores the number as text, whole, so <c>1.005m</c> in a column
///         declared <c>HasPrecision(18, 2)</c> comes back <c>1.005m</c> where a server gives <c>1.01m</c>, and a number
///         too large for the column is kept where a server refuses the save — a test on SQLite would pass on a number
///         production never holds. A converter rounds the number on its way to the database, and throws for one too
///         large, which the save reports as a <see cref="DbUpdateException" /> as a server's refusal is. Reading it back
///         is the number as stored. A precision with no scale is a scale of 0, as both servers read <c>decimal(p)</c>.
///     </para>
///     <para>
///         Runs before EF assigns type mappings, so the converter is the property's from the start. A column with no
///         declared precision, a store type of its own, or a converter of its own (a value object's, whose own rules hold
///         its digits) is left as it is, and a JSON document's members are no columns.
///     </para>
/// </remarks>
internal sealed class DecimalScaleConvention : IModelFinalizingConvention
{
    // decimal holds 28 or 29 significant digits: a column with room for more whole digits than that never overflows.
    private const int WholeDigitsDecimalHolds = 28;

    private static readonly ConcurrentDictionary<(int Precision, int Scale), ValueConverter<decimal, decimal>> ByFacets = new();

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().Where(static e => !e.IsMappedToJson()))
        {
            Hold(entityType.GetDeclaredProperties(), entityType.GetDeclaredComplexProperties());
        }
    }

    private static void Hold(IEnumerable<IConventionProperty> properties, IEnumerable<IConventionComplexProperty> complexProperties)
    {
        foreach (var property in properties)
        {
            if (FacetsOf(property) is { } facets)
            {
                property.Builder.HasConversion(ByFacets.GetOrAdd(facets, static f => Converter(f.Precision, f.Scale)));
            }
        }

        foreach (var type in complexProperties.Select(static c => c.ComplexType).Where(static t => !t.IsMappedToJson()))
        {
            Hold(type.GetDeclaredProperties(), type.GetDeclaredComplexProperties());
        }
    }

    // The precision and scale a server would hold the column's value to, or null for a column this leaves alone.
    private static (int Precision, int Scale)? FacetsOf(IConventionProperty property) =>
        (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(decimal)
        && property.GetValueConverter() is null
        && property.GetProviderClrType() is null
        && property.GetColumnType() is null
        && property.GetPrecision() is { } precision
            ? (precision, property.GetScale() ?? 0)
            : null;

    private static ValueConverter<decimal, decimal> Converter(int precision, int scale)
    {
        var whole = precision - scale;
        decimal? limit = whole is >= 0 and < WholeDigitsDecimalHolds ? Power(whole) : null;
        return new(v => Held(v, precision, scale, limit), v => v);
    }

    private static decimal Held(decimal value, int precision, int scale, decimal? limit)
    {
        var rounded = decimal.Round(value, scale, MidpointRounding.AwayFromZero);
        return limit is { } room && Math.Abs(rounded) >= room
            ? throw new ArgumentOutOfRangeException(
                nameof(value),
                rounded,
                string.Create(CultureInfo.InvariantCulture, $"The value is out of range of decimal({precision}, {scale}): it has more whole digits than {precision - scale}."))
            : rounded;
    }

    private static decimal Power(int digits)
    {
        var power = 1m;
        for (var i = 0; i < digits; i++)
        {
            power *= 10;
        }

        return power;
    }
}
