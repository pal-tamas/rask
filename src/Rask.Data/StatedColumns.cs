using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Core.Forms;

namespace Rask.Data;

/// <summary>
///     Gives a column what the rules a validation library states for its property say — FluentValidation's
///     <c>MaximumLength(200)</c> a length of 200, <c>NotEmpty()</c> NOT NULL, <c>PrecisionScale(10, 2)</c>
///     <c>decimal(10, 2)</c> — exactly as the DataAnnotations attribute with the same rule gives it, so a migration
///     shows it.
/// </summary>
/// <remarks>
///     At convention strength: an attribute on the property (<c>[MaxLength]</c>, <c>[Precision]</c>) is the app's word
///     for this one column and wins (RASK182 says when they disagree), and the entity's own <c>Configure</c>, which runs
///     after this, has the last word.
/// </remarks>
internal static class StatedColumns
{
    internal static void Apply(List<IMutableEntityType> entityTypes)
    {
        if (StatedRules.Source is null)
        {
            return;
        }

        foreach (var entityType in entityTypes.Where(static e => StatedRules.States(e.ClrType)))
        {
            foreach (var property in entityType.GetDeclaredProperties().Where(static p => !p.IsShadowProperty()).ToList())
            {
                Apply((IConventionProperty)property, StatedRules.FacetsOf(entityType.ClrType, property.Name));
            }
        }
    }

    private static void Apply(IConventionProperty property, StatedFacets facets)
    {
        if (facets.IsEmpty || property.Builder is not { } column)
        {
            return;
        }

        if (facets.MaxLength is { } length && property.ClrType == typeof(string))
        {
            column.HasMaxLength(length);
        }

        if (facets.Required && property.IsNullable && !property.ClrType.IsValueType)
        {
            column.IsRequired(true);
        }

        if (facets.Precision is { } precision && IsDecimal(property.ClrType))
        {
            column.HasPrecision(precision);
            column.HasScale(facets.Scale ?? 0);
        }
    }

    private static bool IsDecimal(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(decimal);
}
