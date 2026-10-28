using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Translations;

namespace Rask.Generators.Tests;

// The curated plural table against CLDR itself, not against a transcription of it.
//
// PluralRules carries each language's picker as C# source. Here every picker is compiled and run for each whole
// count up to 10,000, and each answer is held to what CLDR's own rule says for that count (cldr-plurals.json,
// which the cldr-plurals follower moves to CLDR's latest release). When CLDR changes a carried language, the
// follower's commit fails here and its issue opens: a person decides what the table says.
//
// Whole counts only, which is all a count can be — and up to 10,000, because CLDR's "many" for an exact million
// (French, Spanish, Italian, Portuguese, Catalan) is a form the table does not carry yet.
public class PluralRulesMatchCldrTests
{
    private const long Highest = 10_000;

    public static TheoryData<string> Languages() => new(PluralRules.Languages.OrderBy(l => l, StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_carried_language_picks_the_category_CLDR_gives_each_whole_count(string language)
    {
        var cldr = Cldr.Value.GetProperty("cardinal").GetProperty(language);
        var categories = PluralRules.CategoriesFor(language)!;
        var pick = Pickers.Value[language];

        var wrong = Counts()
            .Select(n => (n, Expected: CategoryOf(cldr, n), Actual: categories[pick(n)]))
            .Where(c => c.Expected != c.Actual)
            .Take(5)
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{language}: " + string.Join(", ", wrong.Select(c => $"{c.n} is {c.Expected} in CLDR, {c.Actual} here")));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_category_a_carried_language_lists_is_one_a_whole_count_reaches(string language)
    {
        var cldr = Cldr.Value.GetProperty("cardinal").GetProperty(language);

        var reached = Counts().Select(n => CategoryOf(cldr, n)).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(reached.OrderBy(c => c, StringComparer.Ordinal),
            PluralRules.CategoriesFor(language)!.OrderBy(c => c, StringComparer.Ordinal));
    }

    private static IEnumerable<long> Counts()
    {
        for (long n = 0; n <= Highest; n++)
        {
            yield return n;
        }
    }

    private static readonly Lazy<JsonElement> Cldr = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cldr-plurals.json"))).RootElement);

    // The category CLDR's rules give a whole count: the first non-empty rule that holds, else "other".
    private static string CategoryOf(JsonElement rules, long n)
    {
        foreach (var rule in rules.EnumerateObject())
        {
            if (rule.Value.GetString() is { Length: > 0 } text && Holds(text, n))
            {
                return rule.Name;
            }
        }

        return "other";
    }

    // CLDR's rule syntax over a whole count: `or` of `and` of `operand [% m] (=|!=) a..b,c`. For an integer the
    // operands n and i are the count, and v, w, f, t, c and e (fraction digits and exponent) are 0.
    private static bool Holds(string rule, long n) =>
        rule.Split(" or ").Any(all => all.Split(" and ").All(relation => Relation(relation, n)));

    private static bool Relation(string relation, long n)
    {
        var negated = relation.Contains("!=", StringComparison.Ordinal);
        var sides = relation.Split(negated ? "!=" : "=");
        var left = sides[0].Trim().Split('%');
        var value = left[0].Trim() is "n" or "i" ? n : 0;
        if (left.Length == 2)
        {
            value %= long.Parse(left[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        var inRange = sides[1].Split(',').Any(range =>
        {
            var bounds = range.Trim().Split("..");
            var low = long.Parse(bounds[0], System.Globalization.CultureInfo.InvariantCulture);
            var high = bounds.Length == 2 ? long.Parse(bounds[1], System.Globalization.CultureInfo.InvariantCulture) : low;
            return value >= low && value <= high;
        });

        return inRange != negated;
    }

    // Every language's picker body, compiled as written, as one method each.
    private static readonly Lazy<Dictionary<string, Func<long, int>>> Pickers = new(() =>
    {
        var languages = PluralRules.Languages.ToList();
        var source = "public static class Pickers {\n"
            + string.Concat(languages.Select(l => $"public static int {l}(long n) {{ {PluralRules.BodyFor(l)} }}\n"))
            + "}";

        var compilation = CSharpCompilation.Create(
            "PluralPickers",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var type = Assembly.Load(image.ToArray()).GetType("Pickers")!;
        return languages.ToDictionary(
            l => l,
            l => type.GetMethod(l)!.CreateDelegate<Func<long, int>>(),
            StringComparer.OrdinalIgnoreCase);
    });
}
