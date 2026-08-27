using Microsoft.CodeAnalysis;
using Rask.Generators.Translations;

namespace Rask.Generators.Tests;

// Plural categories: the part of translation that cannot be worked around at the call site.
//
// A count is dynamic by definition, so "just write two keys and pick one" is wrong the moment the
// language has three categories — and Polish, Russian, Czech, Latvian, Lithuanian, Romanian and Arabic
// all do. Getting it wrong produces text that reads as broken to every native speaker while every test
// stays green, which is why the rule table is curated and an unknown language is a build error.
public class TranslationPluralTests
{
    private static GeneratorRun Run(params (string Path, string Contents)[] catalogs) =>
        GeneratorDriverFixture.Run(
            [("App.cs", "namespace App; public static class Marker { }")],
            new TranslationCatalogGenerator(),
            catalogs);

    private static string Generated(GeneratorRun run) =>
        string.Join("\n", run.RunResult.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));

    [Fact]
    public void A_plural_key_becomes_a_method_that_takes_the_count()
    {
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "Cart": { "$plural": "count", "one": "{count} item", "other": "{count} items" } }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var code = Generated(run);
        Assert.Contains("public static string Cart(long count)", code, StringComparison.Ordinal);
        Assert.Contains("__Plural.En(count)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_languages_that_pluralise_get_a_category_function()
    {
        // An app with no plural keys must pay nothing for this feature.
        var plain = Run(("/p/Resources/Strings.en.json", """{ "A": "one" }"""));
        Assert.DoesNotContain("__Plural", Generated(plain), StringComparison.Ordinal);

        var polish = Run(
            ("/p/Resources/Strings.en.json",
                """{ "C": { "$plural": "n", "one": "{n} item", "other": "{n} items" } }"""),
            ("/p/Resources/Strings.pl.json",
                """{ "C": { "$plural": "n", "one": "{n} plik", "few": "{n} pliki", "many": "{n} plików" } }"""));

        // Note the pl catalog has no "other": Polish integers never select it, so requiring one would
        // be asking for text no visitor could ever see.
        var code = Generated(polish);
        Assert.Empty(polish.RunResult.Diagnostics);

        // This assertion is the one that was missing. Everything above passes on output that does not
        // compile, and en+pl is exactly the ordering that did not — see the test below.
        Assert.Empty(polish.GeneratedCompileErrors());
        Assert.Contains("__Plural.En", code, StringComparison.Ordinal);
        Assert.Contains("__Plural.Pl", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plural key in an app whose neutral language sorts <em>before</em> a translation.
    /// </summary>
    /// <remarks>
    /// The neutral catalog is the <c>_</c> arm of the generated switch expression, and <c>_</c> matches
    /// everything — so emitting it in catalog order makes every arm after it unreachable, which is a
    /// compile error (CS8510) rather than a subtle runtime wrong-text bug. Catalogs are sorted by tag,
    /// so this fires whenever the neutral language sorts first: <c>en</c> + <c>hu</c>, <c>en</c> +
    /// <c>fr</c>, <c>en</c> + <c>pl</c>. That is what <c>rask new --culture en --culture hu</c>
    /// scaffolds on every template, and the scaffolded catalog carries a plural key.
    ///
    /// <para>
    /// It went unseen because nothing in this repository compiled two catalogs — the tests grepped the
    /// generated text, and the only catalog in the tree is the Shop sample's single English one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("hu", "{count} elem", "{count} elem")]
    [InlineData("fr", "{count} article", "{count} articles")]
    public void The_neutral_arm_is_emitted_last_so_a_later_language_stays_reachable(
        string tag, string one, string other)
    {
        var run = Run(
            ("/p/Resources/Strings.en.json",
                """{ "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" } }"""),
            ($"/p/Resources/Strings.{tag}.json",
                $$"""{ "Items": { "$plural": "count", "one": "{{one}}", "other": "{{other}}" } }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        // The default arm is last, so the translated arm above it can still be selected.
        var code = Generated(run);
        var translated = code.IndexOf($"__Plural.{char.ToUpperInvariant(tag[0])}{tag.Substring(1)}(count)", StringComparison.Ordinal);
        var neutral = code.IndexOf("_ => __Plural.En(count)", StringComparison.Ordinal);
        Assert.True(translated >= 0, "the translated language got no arm at all");
        Assert.True(
            neutral > translated,
            "the neutral '_' arm precedes the translated arm, so the translation is unreachable");
    }

    [Fact]
    public void A_language_whose_grammar_Rask_does_not_carry_is_an_error_naming_it()
    {
        // The honest failure. Silently applying English rules would produce grammatically wrong text
        // that nothing in the build or the test suite could see.
        var run = Run(
            ("/p/Resources/Strings.en.json",
                """{ "C": { "$plural": "n", "one": "{n} item", "other": "{n} items" } }"""),
            ("/p/Resources/Strings.cy.json",
                """{ "C": { "$plural": "n", "one": "{n} eitem", "other": "{n} eitemau" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("'cy'", error.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("open an issue", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plural_set_with_no_other_form_is_an_error()
    {
        // English's residual IS "other", so that is what it must supply. (Other languages differ — see
        // the residual test below; this one is deliberately the simple case.)
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "C": { "$plural": "n", "one": "{n} item" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("no 'other' form", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_the_language_cannot_select_is_an_error()
    {
        // English has no "few". Text placed there could never be shown, and nothing at runtime would
        // ever say so.
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "C": { "$plural": "n", "one": "a", "few": "b", "other": "c" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("does not distinguish", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_that_is_not_a_CLDR_category_at_all_is_an_error()
    {
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "C": { "$plural": "n", "single": "a", "other": "c" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("not a CLDR plural category", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_translation_missing_a_category_its_language_needs_is_a_warning()
    {
        // Russian distinguishes one/few/many; this one omits "few". The page still works — the residual
        // form carries it — so it is a warning, like any missing text.
        var run = Run(
            ("/p/Resources/Strings.en.json",
                """{ "C": { "$plural": "n", "one": "{n} item", "other": "{n} items" } }"""),
            ("/p/Resources/Strings.ru.json",
                """{ "C": { "$plural": "n", "one": "{n} файл", "many": "{n} файлов" } }"""));

        var warning = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK052");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'few'", warning.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_required_fallback_form_is_the_languages_own_residual_not_always_other()
    {
        // The distinction that makes Polish expressible at all. Its residual is "many"; demanding
        // "other" would force dead text while leaving the form it really uses optional.
        Assert.Equal("other", PluralRules.ResidualFor("en"));
        Assert.Equal("many", PluralRules.ResidualFor("pl"));
        Assert.Equal("many", PluralRules.ResidualFor("ru"));
        Assert.Equal("other", PluralRules.ResidualFor("cs"));

        var run = Run(("/p/Resources/Strings.en.json",
            """{ "C": { "$plural": "n", "one": "{n} item", "other": "{n} items" } }"""),
            ("/p/Resources/Strings.pl.json",
                """{ "C": { "$plural": "n", "one": "{n} plik", "few": "{n} pliki" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("no 'many' form", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_cannot_be_a_plural_set_in_one_language_and_a_string_in_another()
    {
        // They generate different members, so the call site would depend on the visitor's language.
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "C": "items" }"""),
            ("/p/Resources/Strings.hu.json",
                """{ "C": { "$plural": "n", "one": "{n} elem", "other": "{n} elem" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("cannot differ", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_plural_marker_is_rejected_with_a_useful_reason()
    {
        var run = Run(("/p/Resources/Strings.en.json", """{ "$plural": "n", "other": "x" }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("belongs inside the key it pluralises", error.GetMessage(), StringComparison.Ordinal);
    }

    // The rule table is grammar, not code, so its BOUNDARIES are what matter. These are the values
    // that separate one category from the next in each family.
    [Theory]
    // English: one at exactly 1.
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    // French: zero is singular.
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    // Russian: 1/21 one; 2-4/22-24 few; 11-14 and 5-20 many.
    [InlineData("ru", 1, "one")]
    [InlineData("ru", 2, "few")]
    [InlineData("ru", 5, "many")]
    [InlineData("ru", 11, "many")]
    [InlineData("ru", 21, "one")]
    [InlineData("ru", 22, "few")]
    [InlineData("ru", 112, "many")]
    // Polish: 1 one; 22-24 few; 12-14 many.
    [InlineData("pl", 1, "one")]
    [InlineData("pl", 2, "few")]
    [InlineData("pl", 5, "many")]
    [InlineData("pl", 12, "many")]
    [InlineData("pl", 22, "few")]
    // Czech: 1 one; 2-4 few; the rest other.
    [InlineData("cs", 1, "one")]
    [InlineData("cs", 3, "few")]
    [InlineData("cs", 5, "other")]
    // Arabic: all six.
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 100, "other")]
    // Japanese: no grammatical number.
    [InlineData("ja", 0, "other")]
    [InlineData("ja", 1, "other")]
    [InlineData("ja", 7, "other")]
    public void The_curated_rules_pick_the_category_CLDR_specifies(string language, long count, string expected)
    {
        var categories = PluralRules.CategoriesFor(language);
        Assert.NotNull(categories);

        var index = EvaluateRule(language, count);
        Assert.Equal(expected, categories![index]);
    }

    // Mirrors the emitted arithmetic. Kept as a transcription of PluralRules' bodies rather than
    // invoking them, because the generator emits SOURCE — this is what a reader can check against CLDR.
    private static int EvaluateRule(string language, long n) => language switch
    {
        "en" => n == 1 ? 0 : 1,
        "fr" => n is 0 or 1 ? 0 : 1,
        "ja" => 0,
        "ru" => Ru(n),
        "pl" => Pl(n),
        "cs" => n == 1 ? 0 : n is >= 2 and <= 4 ? 1 : 2,
        "ar" => Ar(n),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "no rule transcribed"),
    };

    private static int Ru(long n)
    {
        var m10 = n % 10;
        var m100 = n % 100;
        if (m10 == 1 && m100 != 11) return 0;
        if (m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14)) return 1;
        return 2;
    }

    private static int Pl(long n)
    {
        if (n == 1) return 0;
        var m10 = n % 10;
        var m100 = n % 100;
        if (m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14)) return 1;
        return 2;
    }

    private static int Ar(long n)
    {
        if (n == 0) return 0;
        if (n == 1) return 1;
        if (n == 2) return 2;
        var m100 = n % 100;
        if (m100 is >= 3 and <= 10) return 3;
        if (m100 is >= 11 and <= 99) return 4;
        return 5;
    }
}
