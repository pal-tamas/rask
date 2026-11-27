using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Stores every <see cref="Moment" /> value object as the UTC instant it holds, on every provider Rask maps: SQL
///     Server's <c>datetime2</c>, SQLite's text and PostgreSQL's <c>timestamptz</c> (which refuses a wall-clock time
///     outright) — so the same moment is saved, read, compared and ordered alike on every database, the hour the clock
///     shows twice in autumn included.
/// </summary>
/// <remarks>
///     <para>
///         Runs first among the finalizing conventions, before EF Core picks each column's type mapping from its
///         converter. Rask's own converter stores the instant itself (<see cref="OneValueConverters.StoringInstant" />);
///         a converter the app gave the property is composed with <see cref="WallClockInUtc" />. A bare
///         <see cref="DateTime" /> is left as the provider maps it.
///     </para>
///     <para>
///         Where moments were kept as the wall clock before (SQL Server and SQLite until Rask stored them as UTC), each
///         column is marked <see cref="MomentsInUtc.Annotation" />, which the migration snapshot keeps: the first migration added
///         after the change rewrites the column's rows from the wall clock to UTC (<see cref="MomentsInUtc" />), and until
///         it is added the app's start refuses the model as changed since its last migration. A moment inside a JSON
///         document keeps the wall clock there, as it always did: no migration rewrites a document.
///     </para>
/// </remarks>
/// <param name="rewritesWallClock">Whether the provider kept moments as the wall clock before, so a migration rewrites them.</param>
internal sealed class UtcMomentConvention(bool rewritesWallClock) : IModelFinalizingConvention
{

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            Store(entityType, entityType.IsMappedToJson());
        }
    }

    private void Store(IConventionTypeBase type, bool json)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            var held = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!typeof(Moment).IsAssignableFrom(held) ||
                property.GetValueConverter() is not { ProviderClrType: var stored } converter ||
                stored != typeof(DateTime) ||
                OneValueConverters.StoredAs(converter) is not null)
            {
                continue;
            }

            // A moment inside a JSON document keeps the wall clock where it always did: no migration rewrites a document.
            if (rewritesWallClock && json)
            {
                continue;
            }

            var instant = OneValueConverters.StoringInstant(converter) ?? converter.ComposeWith(WallClockInUtc.Converter);
            ((IMutableProperty)property).SetValueConverter(instant);
            // A timestamp Rask writes was always the UTC instant: its column has nothing to rewrite.
            if (rewritesWallClock && held != typeof(Timestamp))
            {
                property.Builder.HasAnnotation(MomentsInUtc.Annotation, true);
            }
        }

        foreach (var complex in type.GetDeclaredComplexProperties().Select(static c => c.ComplexType))
        {
            Store(complex, json || complex.IsMappedToJson());
        }
    }
}
