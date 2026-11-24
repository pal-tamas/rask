using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.CodeFixes;

// The base-record arguments a DataAnnotations attribute says: [StringLength(200, MinimumLength = 2)] is MaxLength: 200,
// MinLength: 2, and [Range(0, 100)] is Min: 0, Max: 100. False for an attribute no argument carries whole — one with a
// message of its own, say — which the fix then leaves alone by not being offered.
internal static class AttributeArguments
{
    public static bool Read(AttributeData attribute, string name, ValueBase valueBase, BaseArguments arguments)
    {
        var given = attribute.ConstructorArguments;
        if (valueBase.IsText)
        {
            return name switch
            {
                "StringLengthAttribute" when given.Length == 1 && OnlyNamed(attribute, "MinimumLength") =>
                    Length(arguments, "MaxLength", given[0].Value) &&
                    attribute.NamedArguments.All(n => Length(arguments, "MinLength", n.Value.Value)),
                "MaxLengthAttribute" when given.Length == 1 && OnlyNamed(attribute) => Length(arguments, "MaxLength", given[0].Value),
                "MinLengthAttribute" when given.Length == 1 && OnlyNamed(attribute) => Length(arguments, "MinLength", given[0].Value),
                _ => false,
            };
        }

        if (!valueBase.IsNumeric || !string.Equals(name, "RangeAttribute", StringComparison.Ordinal) || !OnlyNamed(attribute))
        {
            return false;
        }

        // Two numbers, or a type and two texts that parse as numbers.
        var (min, max) = given.Length switch
        {
            2 => (BaseArguments.Literal(given[0].Value, valueBase.IsWhole), BaseArguments.Literal(given[1].Value, valueBase.IsWhole)),
            3 => (Parsed(given[1].Value, valueBase), Parsed(given[2].Value, valueBase)),
            _ => (null, null),
        };

        if (min is null || max is null)
        {
            return false;
        }

        arguments.Set("Min", min);
        arguments.Set("Max", max);
        return true;
    }

    private static bool OnlyNamed(AttributeData attribute, params string[] allowed) =>
        attribute.NamedArguments.All(n => allowed.Contains(n.Key, StringComparer.Ordinal));

    private static bool Length(BaseArguments arguments, string parameter, object? value)
    {
        if (value is not int length || length < 0)
        {
            return false;
        }

        if (length > 0)
        {
            arguments.Set(parameter, length.ToString(CultureInfo.InvariantCulture));
        }

        return true;
    }

    private static string? Parsed(object? value, ValueBase valueBase) =>
        value is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? BaseArguments.Literal(number, valueBase.IsWhole)
            : null;
}
