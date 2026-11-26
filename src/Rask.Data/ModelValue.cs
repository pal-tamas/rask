using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>A form model's value for a property of its entity, in the property's own type.</summary>
internal static class ModelValue
{
    /// <summary>
    ///     <paramref name="value" /> as <paramref name="property" /> holds it: a one-value value object is its value on the
    ///     form and itself in the model, and the column's own converter turns the one into the other.
    /// </summary>
    internal static object? InPropertyType(IProperty property, object value) =>
        !property.ClrType.IsInstanceOfType(value) &&
        property.GetValueConverter() is { } converter &&
        converter.ProviderClrType.IsInstanceOfType(value)
            ? converter.ConvertFromProvider(value)
            : value;
}
