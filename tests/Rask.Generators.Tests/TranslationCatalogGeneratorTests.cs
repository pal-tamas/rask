using Microsoft.CodeAnalysis;
using Rask.Generators.Translations;

namespace Rask.Generators.Tests;

// The catalog generator's contract: a key that exists becomes a member, a key that does not becomes a
// C# compile error at the call site, and a translation that disagrees with the neutral catalog is
// reported before it can throw at runtime.
public class TranslationCatalogGeneratorTests
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
    public void A_key_becomes_a_member_and_the_generated_code_compiles()
    {
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "Greeting": "Hello!" }"""),
            ("/p/Resources/Strings.hu.json", """{ "Greeting": "Szia!" }"""));

        Assert.Empty(run.RunResult.Diagnostics);

        // The strongest assertion available: the emitted switch, the escaped literals and every
        // identifier actually bind.
        Assert.Empty(run.GeneratedCompileErrors());

        var code = Generated(run);
        Assert.Contains("public static partial class Strings", code, StringComparison.Ordinal);
        Assert.Contains("\"Szia!\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_becomes_a_typed_parameter()
    {
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "Greeting": "Hello, {name}!", "Cart": "{count:int} items", "Due": "{when:DateOnly:d}" }"""));

        var code = Generated(run);
        Assert.Empty(run.GeneratedCompileErrors());

        // Named, so the signature documents itself; typed where the catalog says so.
        Assert.Contains("public static string Greeting(object? name)", code, StringComparison.Ordinal);
        Assert.Contains("public static string Cart(int count)", code, StringComparison.Ordinal);
        Assert.Contains("public static string Due(global::System.DateOnly when)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_with_no_placeholders_is_a_property_so_reading_it_allocates_nothing()
    {
        var run = Run(("/p/Resources/Strings.en.json", """{ "Title": "Dashboard" }"""));

        Assert.Contains("public static string Title => __Index switch", Generated(run), StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_objects_become_nested_classes()
    {
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "Home": { "Title": "Dashboard", "Sub": { "Deep": "x" } } }"""));

        var code = Generated(run);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static class Home", code, StringComparison.Ordinal);
        Assert.Contains("public static class Sub", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_translation_missing_a_key_is_a_warning_and_falls_back_to_the_neutral_text()
    {
        // The normal steady state of every real project, so a warning rather than an error.
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "A": "one", "B": "two" }"""),
            ("/p/Resources/Strings.hu.json", """{ "A": "egy" }"""));

        var warning = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK052");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("no translation for 'B'", warning.GetMessage(), StringComparison.Ordinal);

        // It still generates, and B answers in the neutral language.
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"two\"", Generated(run), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_only_a_translation_has_is_a_warning_because_it_generates_nothing()
    {
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "A": "one" }"""),
            ("/p/Resources/Strings.hu.json", """{ "A": "egy", "Extra": "többlet" }"""));

        var warning = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK052");
        Assert.Contains("'Extra' is not in the neutral catalog", warning.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_set_that_disagrees_is_an_error_not_a_warning()
    {
        // string.Format would throw FormatException the first time that string rendered — in one
        // language only, which is the worst possible place to discover it.
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "Greeting": "Hello, {name}!" }"""),
            ("/p/Resources/Strings.hu.json", """{ "Greeting": "Szia, {nev}!" }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("FormatException", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reordering_placeholders_in_a_translation_is_fine()
    {
        // Which is the whole reason placeholders are named: other languages move the arguments.
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "M": "{a} then {b}" }"""),
            ("/p/Resources/Strings.hu.json", """{ "M": "{b} majd {a}" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Theory]
    [InlineData("""{ "A": "one", "A": "two" }""", "duplicate key")]
    [InlineData("""{ "A": 42 }""", "not text or a nested object")]
    [InlineData("""{ "A": "one" """, "expected ',' or '}'")]
    [InlineData("""{ "A": "an unclosed {brace" }""", "unclosed")]
    [InlineData("""{ "A": "mixed {0} and {name}" }""", "one or the other")]
    public void A_catalog_that_cannot_generate_correct_code_is_an_error(string json, string expected)
    {
        var run = Run(("/p/Resources/Strings.en.json", json));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains(expected, error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_family_with_no_neutral_catalog_says_which_file_is_missing()
    {
        var run = Run(("/p/Resources/Strings.hu.json", """{ "A": "egy" }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("Strings.en.json", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_file_that_is_not_a_catalog_is_ignored_without_complaint()
    {
        // An app's own data sitting in Resources/. An "orphan file" diagnostic here would fire on every
        // seed file in the repo and teach nothing.
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "A": "one" }"""),
            ("/p/Resources/seed-data.json", """{ "not": "a catalog" }"""),
            ("/p/appsettings.json", """{ "Logging": {} }"""));

        Assert.Empty(run.RunResult.Diagnostics);
    }

    [Fact]
    public void Two_families_generate_two_independent_classes()
    {
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "A": "one" }"""),
            ("/p/Resources/Validation.en.json", """{ "Required": "This field is required." }"""));

        var code = Generated(run);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("class Strings", code, StringComparison.Ordinal);
        Assert.Contains("class Validation", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_falls_back_through_its_parent_language()
    {
        var run = Run(
            ("/p/Resources/Strings.en.json", """{ "A": "one" }"""),
            ("/p/Resources/Strings.hu.json", """{ "A": "egy" }"""));

        // hu-HU is not a catalog, so the emitted resolver has to walk to hu before giving up on the
        // neutral language.
        Assert.Contains("LastIndexOf('-')", Generated(run), StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_and_braces_survive_into_the_generated_literal()
    {
        var run = Run(("/p/Resources/Strings.en.json",
            """{ "Quote": "He said \"hi\"", "Brace": "{{literal}}", "Tab": "a\tb" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
