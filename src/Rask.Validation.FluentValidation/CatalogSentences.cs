using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Rask.Core.Globalization;

namespace Rask;

// Which RaskStrings sentence a FluentValidation built-in rule says: the one the equivalent DataAnnotations attribute
// says through Rask (AttributeMessages) — NotEmpty what [Required] says, MaximumLength what [StringLength]/[MaxLength]
// says, InclusiveBetween what [Range] says. A catalog sentence numbers its values ({0}, {1}) and FluentValidation names
// them, so each key also lists the placeholder each number stands for. A rule the catalog has no sentence for
// (GreaterThan, ExactLength, ScalePrecision, Must …) is not here, and keeps FluentValidation's own words.
internal static class CatalogSentences
{
    private static readonly Dictionary<string, (RaskString Key, string[] Values)> _sentences = new(StringComparer.Ordinal)
    {
        ["NotNullValidator"] = (RaskString.ValidationRequired, []),
        ["NotEmptyValidator"] = (RaskString.ValidationRequired, []),
        ["MaximumLengthValidator"] = (RaskString.ValidationTooLong, ["MaxLength"]),
        ["MaximumLength_Simple"] = (RaskString.ValidationTooLong, ["MaxLength"]),
        ["MinimumLengthValidator"] = (RaskString.ValidationTooShort, ["MinLength"]),
        ["MinimumLength_Simple"] = (RaskString.ValidationTooShort, ["MinLength"]),
        ["LengthValidator"] = (RaskString.ValidationLength, ["MinLength", "MaxLength"]),
        ["Length_Simple"] = (RaskString.ValidationLength, ["MinLength", "MaxLength"]),
        ["InclusiveBetweenValidator"] = (RaskString.ValidationBetween, ["From", "To"]),
        ["InclusiveBetween_Simple"] = (RaskString.ValidationBetween, ["From", "To"]),
        ["ExclusiveBetweenValidator"] = (RaskString.ValidationAboveAndBelow, ["From", "To"]),
        ["GreaterThanOrEqualValidator"] = (RaskString.ValidationNumberMin, ["ComparisonValue"]),
        ["LessThanOrEqualValidator"] = (RaskString.ValidationNumberMax, ["ComparisonValue"]),
        ["EmailValidator"] = (RaskString.ValidationEmail, []),
        ["CreditCardValidator"] = (RaskString.ValidationCreditCard, []),
        ["RegularExpressionValidator"] = (RaskString.ValidationPattern, []),
        ["NotEqualValidator"] = (RaskString.ValidationNotAllowed, []),
        ["EnumValidator"] = (RaskString.ValidationInvalid, []),
        ["StringEnumValidator"] = (RaskString.ValidationInvalid, []),
    };

    // A catalog sentence rewritten with FluentValidation's names, once per sentence: bounded by the app's catalogs.
    private static readonly ConcurrentDictionary<(string Validator, string Sentence), string?> _templates = new();

    /// <summary>
    ///     The catalog's sentence for FluentValidation's <paramref name="validator" /> key in <paramref name="culture" />,
    ///     as a FluentValidation message template, or null where the catalog has none in that language.
    /// </summary>
    internal static string? For(string validator, CultureInfo culture)
    {
        if (!_sentences.TryGetValue(validator, out var sentence)
            || RaskStrings.Translation(sentence.Key, culture) is not { } said)
        {
            return null;
        }

        return _templates.GetOrAdd((validator, said), static (key, values) => Named(key.Sentence, values), sentence.Values);
    }

    // "Enter at most {0} characters." as "Enter at most {MaxLength} characters.", a format kept: {0:N0} → {MaxLength:N0}.
    // A sentence that asks for a value the rule has none for, or whose braces do not parse, is passed over for
    // FluentValidation's own: a translation's slip never shows the reader a stray "{2}".
    private static string? Named(string sentence, string[] values)
    {
        var named = new StringBuilder(sentence.Length + 16);
        var at = 0;
        while (at < sentence.Length)
        {
            var c = sentence[at];
            var doubled = at + 1 < sentence.Length && sentence[at + 1] == c;
            if (c is '{' or '}' && doubled)
            {
                named.Append(c);
                at += 2;
            }
            else if (c is not '{')
            {
                named.Append(c);
                at++;
            }
            else
            {
                var close = sentence.IndexOf('}', at);
                if (close < 0 || Placeholder(sentence.AsSpan(at + 1, close - at - 1), values) is not { } placeholder)
                {
                    return null;
                }

                named.Append('{').Append(placeholder).Append('}');
                at = close + 1;
            }
        }

        return named.ToString();
    }

    // "0" as "MaxLength", "0:N0" as "MaxLength:N0"; null for an index the rule has no value for, or an alignment.
    private static string? Placeholder(ReadOnlySpan<char> hole, string[] values)
    {
        var colon = hole.IndexOf(':');
        var index = colon < 0 ? hole : hole[..colon];
        if (!int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number >= values.Length)
        {
            return null;
        }

        return colon < 0 ? values[number] : string.Concat(values[number], hole[colon..]);
    }
}
