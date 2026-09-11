using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.MySql;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as its UTC instant, so it keeps its microseconds on MySQL.
/// </summary>
/// <remarks>
/// <para>
/// Oracle's provider loses a <see cref="DateTimeOffset"/>'s fractional seconds twice over (verified against MySQL
/// 8.4 with MySql.EntityFrameworkCore 10.0.9): it maps an unconfigured one to <c>datetime</c>, which holds whole
/// seconds, and even from a <c>datetime(6)</c> column that does hold microseconds its reader hands back the value
/// truncated to the second. So two events milliseconds apart stop ordering, and a value read back no longer equals
/// the one written. Its <see cref="DateTime"/> path has neither defect: <c>datetime(6)</c>, read back intact.
/// </para>
/// <para>
/// So every <see cref="DateTimeOffset"/> property is stored through that path: converted to its UTC
/// <see cref="DateTime"/> on the way in, and read back as that instant at offset zero. The provider already wrote
/// the UTC instant and read offset zero, so no value's meaning changes — only the lost digits come back. A
/// precision or column type the app configured is kept; a property with a converter of its own is left alone.
/// </para>
/// </remarks>
internal sealed class DateTimeOffsetAsUtcConvention : IModelFinalizingConvention
{
    internal static readonly ValueConverter<DateTimeOffset, DateTime> Converter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            Apply(entityType);
        }
    }

    private static void Apply(IConventionTypeBase type)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            if ((property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                && property.GetValueConverter() is null
                && property.GetProviderClrType() is null)
            {
                property.Builder.HasConversion(Converter);
            }
        }

        foreach (var complexProperty in type.GetDeclaredComplexProperties())
        {
            Apply(complexProperty.ComplexType);
        }
    }
}

/// <summary>Adds <see cref="DateTimeOffsetAsUtcConvention"/> to the model's conventions.</summary>
internal sealed class RaskMySqlConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new DateTimeOffsetAsUtcConvention());
        return conventionSet;
    }
}
