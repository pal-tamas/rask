using Rask.Data;

namespace Rask.Testing;

/// <summary>A text a <see cref="Text" />'s lengths and pattern accept.</summary>
internal static class TextSample
{
    // Long enough to read as a value, short enough for most limits; a MinLength asks for more.
    private const int UsualLength = 8;
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string For(string name, TextRules rules)
    {
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
