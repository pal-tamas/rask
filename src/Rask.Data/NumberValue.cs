using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>A row's number between the whole number Rask counts with and the property's own type (a value object's too).</summary>
internal static class NumberValue
{
    /// <summary>The whole number <paramref name="value" /> of <paramref name="property" /> holds; null for none.</summary>
    internal static long? Read(IProperty property, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var stored = property.GetTypeMapping().Converter is { } converter && !IsWhole(value) ? converter.ConvertToProvider(value) : value;
        return stored is null ? null : Convert.ToInt64(stored, CultureInfo.InvariantCulture);
    }

    /// <summary><paramref name="number" /> in <paramref name="property" />'s own type; null for none.</summary>
    internal static object? Write(IProperty property, long? number)
    {
        if (number is not { } value)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (IsWholeType(type))
        {
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        var converter = property.GetTypeMapping().Converter ?? throw new InvalidOperationException(
            $"Numbered({property.DeclaringType.ClrType.Name}.{property.Name}) needs a whole number, or a value object stored as one.");
        var stored = Convert.ChangeType(value, Nullable.GetUnderlyingType(converter.ProviderClrType) ?? converter.ProviderClrType, CultureInfo.InvariantCulture);
        return converter.ConvertFromProvider(stored);
    }

    private static bool IsWhole(object value) => IsWholeType(value.GetType());

    private static bool IsWholeType(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
}
