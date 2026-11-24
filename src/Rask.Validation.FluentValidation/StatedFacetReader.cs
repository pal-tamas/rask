using System.Reflection;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using Rask.Core.Forms;

namespace Rask;

// What a validator's rules for one member say about its field, read off FluentValidation's own descriptor: the checks
// a DataAnnotations attribute has a twin of, and only those that always apply — a rule under When/Unless gives no hint,
// because whether it holds depends on the rest of the object.
internal static class StatedFacetReader
{
    internal static StatedFacets Of<T>(IValidator validator, string member)
    {
        var facets = StatedFacets.None;
        foreach (var rule in validator.CreateDescriptor().GetRulesForMember(member))
        {
            if (rule.HasCondition || rule.HasAsyncCondition)
            {
                continue;
            }

            foreach (var check in rule.Components.Where(static c => c is { HasCondition: false, HasAsyncCondition: false }))
            {
                facets = With<T>(facets, check.Validator);
            }
        }

        return facets;
    }

    private static StatedFacets With<T>(StatedFacets facets, IPropertyValidator check) => check switch
    {
        INotNullValidator or INotEmptyValidator => facets with { Required = true },
        IExactLengthValidator exact => facets with { MaxLength = Bounds.Least(facets.MaxLength, exact.Max), MinLength = Bounds.Most(facets.MinLength, exact.Min) },
        IMaximumLengthValidator most => facets with { MaxLength = Bounds.Least(facets.MaxLength, most.Max) },
        IMinimumLengthValidator least => facets with { MinLength = Bounds.Most(facets.MinLength, least.Min) },
        ILengthValidator length => facets with
        {
            MaxLength = length.Max > 0 ? Bounds.Least(facets.MaxLength, length.Max) : facets.MaxLength,
            MinLength = length.Min > 0 ? Bounds.Most(facets.MinLength, length.Min) : facets.MinLength,
        },
        IEmailValidator => facets with { Email = true },
        RegularExpressionValidator<T> matches => facets with { Pattern = facets.Pattern ?? Pattern(matches) },
        IInclusiveBetweenValidator between when Number(between.From) is { } from && Number(between.To) is { } to =>
            facets with { Min = Bounds.Most(facets.Min, from), Max = Bounds.Least(facets.Max, to) },
        IComparisonValidator { MemberToCompare: null } compared when Number(compared.ValueToCompare) is { } bound =>
            Compared(facets, compared, bound),
        PrecisionScaleValidator<T> digits => facets with
        {
            Precision = Bounds.Least(facets.Precision, digits.Precision),
            Scale = Bounds.Least(facets.Scale, digits.Scale),
        },
        _ => facets,
    };

    // An input's min and max are inclusive: GreaterThan(0) is a min of 1 for a whole number, and no hint for any other.
    private static StatedFacets Compared(StatedFacets facets, IComparisonValidator compared, decimal bound) =>
        compared.Comparison switch
        {
            Comparison.GreaterThanOrEqual => facets with { Min = Bounds.Most(facets.Min, bound) },
            Comparison.LessThanOrEqual => facets with { Max = Bounds.Least(facets.Max, bound) },
            Comparison.GreaterThan when IsWhole(compared.ValueToCompare) => facets with { Min = Bounds.Most(facets.Min, bound + 1) },
            Comparison.LessThan when IsWhole(compared.ValueToCompare) => facets with { Max = Bounds.Least(facets.Max, bound - 1) },
            _ => facets,
        };

    // Matches(...) asks for a match anywhere in the text, and only with the options .NET reads as the browser does: one
    // made case-insensitive or ECMAScript-flavoured tells no pattern. The options sit in the regex the validator makes,
    // which FluentValidation keeps behind its own field; given a fixed expression it is made without the object, so asking
    // for it with none is safe, and a FluentValidation that renamed the field tells no pattern.
    private static string? Pattern<T>(RegularExpressionValidator<T> matches)
    {
        if (matches.Expression is not { } expression)
        {
            return null;
        }

#pragma warning disable S3011 // FluentValidation keeps a Matches rule's RegexOptions only in this private field; without them a case-insensitive rule would be told to the browser as a case-sensitive pattern. Read-only, and a missing field tells no pattern.
        var field = typeof(RegularExpressionValidator<T>).GetField("_regexFunc", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
        if (field?.GetValue(matches) is not Func<T, Regex> made)
        {
            return null;
        }

        try
        {
            return (made(default!).Options & ~(RegexOptions.CultureInvariant | RegexOptions.Compiled)) == RegexOptions.None
                ? HtmlPattern.Found(expression, trimmed: false)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsWhole(object? value) => value is byte or sbyte or short or ushort or int or uint or long or ulong;

    private static decimal? Number(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
        float or double => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) is var d && double.IsFinite(d) && Math.Abs(d) < 7.9e28 ? (decimal)d : null,
        _ => null,
    };
}
