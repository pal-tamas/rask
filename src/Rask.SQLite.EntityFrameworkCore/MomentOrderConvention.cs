using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.SQLite;

/// <summary>
///     Makes every <see cref="DateTimeOffset" /> column of a <c>UseRaskSqlite</c> model compare, order and index by the
///     instant it names — as SQL Server's <c>datetimeoffset</c> does — instead of being refused in a query.
/// </summary>
/// <remarks>
///     <para>
///         EF Core's SQLite provider stores a moment as text with its offset and then refuses <c>OrderBy</c>, <c>&lt;</c>,
///         <c>&gt;</c> and <c>Max</c> over it: text order is not time order across offsets. This keeps the text exactly as
///         EF writes it (<c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c>: every tick, and the offset), maps the column through a
///         converter to that same text, so EF translates the comparison, and creates the column
///         <c>COLLATE "RASK_MOMENT"</c> (<see cref="SqliteCollations.MomentOrder" />), which compares two values by their UTC
///         instant. <c>=</c>, a unique index, <c>ORDER BY</c>, <c>MIN</c>/<c>MAX</c> and a range then answer as on SQL Server:
///         10:00+02:00 and 08:00+00:00 are the same moment.
///     </para>
///     <para>
///         Runs before EF assigns type mappings, so the converter is the property's from the start. A property the model
///         already gave a converter or a collation of its own keeps it, and a JSON document's members are no columns.
///     </para>
/// </remarks>
internal sealed class MomentOrderConvention : IModelFinalizingConvention
{
    private const string Format = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    private static readonly ValueConverter<DateTimeOffset, string> AsText = new(
        static v => v.ToString(Format, CultureInfo.InvariantCulture),
        static v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.None));

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().Where(static e => !e.IsMappedToJson()))
        {
            Collate(entityType.GetDeclaredProperties(), entityType.GetDeclaredComplexProperties());
        }
    }

    private static void Collate(IEnumerable<IConventionProperty> properties, IEnumerable<IConventionComplexProperty> complexProperties)
    {
        foreach (var column in properties.Where(static p => IsMoment(p)).Select(static p => p.Builder))
        {
            column.HasConversion(AsText);
            column.UseCollation(SqliteCollations.MomentOrder);
        }

        foreach (var type in complexProperties.Select(static c => c.ComplexType).Where(static t => !t.IsMappedToJson()))
        {
            Collate(type.GetDeclaredProperties(), type.GetDeclaredComplexProperties());
        }
    }

    private static bool IsMoment(IConventionProperty property) =>
        (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset)
        && property.GetValueConverter() is null
        && property.GetProviderClrType() is null
        && property.GetCollation() is null;
}
