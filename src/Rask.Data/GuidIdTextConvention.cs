using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Writes a typed id over a <see cref="Guid" /> as lower-case text wherever its column is text — SQLite's every
///     <see cref="Guid" />, or a column an existing schema declared <c>nvarchar</c> — instead of the provider's own
///     spelling, which on SQLite is upper case.
/// </summary>
/// <remarks>
///     Lower case is what the id prints, routes and serializes as, and what other tools (Prisma, PostgreSQL's
///     <c>uuid::text</c>) write, so a text key matches the id in every one of them. A column the provider stores a
///     <see cref="Guid" /> in natively (<c>uniqueidentifier</c>, <c>uuid</c>) is left alone.
/// </remarks>
/// <param name="typeMappings">The provider's type mappings, or null when the model is built without one.</param>
internal sealed class GuidIdTextConvention(IRelationalTypeMappingSource? typeMappings) : IModelFinalizingConvention
{
    /// <summary>
    ///     Where the model registry leaves a typed id's text converter: generated beside its <see cref="Guid" /> one, so
    ///     nothing here builds a converter for a type it does not know.
    /// </summary>
    internal const string Annotation = "Rask:GuidIdText";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(static e => e.GetDeclaredProperties()))
        {
            if (property.FindAnnotation(Annotation)?.Value is not ValueConverter text)
            {
                continue;
            }

            // Never left in the model: a migration's snapshot has no way to write a converter instance.
            property.RemoveAnnotation(Annotation);
            if (typeMappings is null || !IsText(property) || !IsTheRegistrysConverter(property))
            {
                continue;
            }

            property.SetValueConverter(text);
            if (property.FindTypeMapping() is not null)
            {
                property.Builder.HasTypeMapping(typeMappings.FindMapping((IProperty)property));
            }
        }
    }

    // A conversion the application configured on the property itself is its own decision.
    private static bool IsTheRegistrysConverter(IConventionProperty property) =>
        property.GetValueConverter() is { ProviderClrType: var provider } && provider == typeof(Guid);

    private bool IsText(IConventionProperty property)
    {
        var storeType = property.GetColumnType() ?? typeMappings!.FindMapping(typeof(Guid))?.StoreType;
        return storeType is not null && typeMappings!.FindMapping(storeType)?.ClrType == typeof(string);
    }
}
