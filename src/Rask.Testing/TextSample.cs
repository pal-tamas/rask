using Rask.Values;

namespace Rask.Testing;

/// <summary>A text a <see cref="Text" />'s lengths and pattern accept.</summary>
internal static class TextSample
{
    // Long enough to read as a value, short enough for most limits; a MinLength asks for more.
    private const int UsualLength = 8;
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string For(string name, TextRules rules)
    {
        if (Formatted(rules) is { } formatted)
        {
            return Fits(formatted, rules)
                ? formatted
                : throw new InvalidOperationException(
                    $"Test.Make<{name}>() built \"{formatted}\" as its {rules.Kind}, which its lengths refuse. "
                    + $"Make this one by hand, with {name}.Create(…).");
        }

        if (rules.Pattern is not { } pattern)
        {
            return Filled(Length(rules));
        }

        var sample = PatternSample.For(pattern)
            ?? throw new InvalidOperationException(
                $"Test.Make<{name}>() cannot build a text for the pattern {pattern}: it meets literals, \\d, \\w, character "
                + $"classes, groups and counted repeats. Make this one by hand, with {name}.Create(…).");

        return Fits(sample, rules)
            ? sample
            : throw new InvalidOperationException(
                $"Test.Make<{name}>() built \"{sample}\" for the pattern {pattern}, which its lengths refuse. "
                + $"Make this one by hand, with {name}.Create(…).");
    }

    // An email address, a phone number or a web address its format accepts, as long as the fewest characters ask:
    // the part that may grow is padded (the name before the @, the digits, the path). Null for a kind with no format.
    private static string? Formatted(TextRules rules)
    {
        var (start, end) = rules.Kind switch
        {
            TextKind.Email => ("sample", "@example.com"),
            TextKind.Phone => ("+36 1 234 567", ""),
            TextKind.Url => ("https://example.com/", ""),
            _ => ((string?)null, ""),
        };

        if (start is null)
        {
            return null;
        }

        var missing = rules.MinLength - start.Length - end.Length;
        var padding = rules.Kind == TextKind.Phone ? new string('8', Math.Max(missing, 0)) : Filled(Math.Max(missing, 0)).ToLowerInvariant();
        return start + padding + end;
    }

    private static int Length(TextRules rules)
    {
        var length = Math.Max(rules.MinLength, Math.Min(UsualLength, rules.MaxLength > 0 ? rules.MaxLength : UsualLength));
        return Math.Max(length, 1);
    }

    private static string Filled(int length) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Letters[i % Letters.Length];
            }
        });

    private static bool Fits(string sample, TextRules rules) =>
        sample.Length > 0 && sample.Length >= rules.MinLength && (rules.MaxLength == 0 || sample.Length <= rules.MaxLength);
}
