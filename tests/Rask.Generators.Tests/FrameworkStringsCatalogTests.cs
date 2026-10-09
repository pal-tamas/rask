using Microsoft.CodeAnalysis;
using Rask.Core.Globalization;
using Rask.Generators.Translations;

namespace Rask.Generators.Tests;

// Resources/RaskStrings.{culture}.json is the reserved catalog an app uses to translate the text Rask
// itself renders — picker chrome, the not-found page, the error page. Its keys are not the app's to
// invent: they are RaskString members.
public class FrameworkStringsCatalogTests
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
    public void The_generator_and_the_runtime_agree_on_which_strings_exist()
    {
        // The generator is a netstandard2.0 analyzer and cannot reference the runtime assembly it
        // generates code for, so it carries its own copy of the key list. This is what stops the two
        // drifting: adding a RaskString member without updating the generator fails here rather than
        // silently making that string untranslatable.
        var runtime = Enum.GetNames<RaskString>().OrderBy(n => n, StringComparer.Ordinal);
        var generator = TranslationCatalogGenerator.FrameworkStringKeysForTests
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(runtime, generator);
    }

    [Fact]
    public void An_app_can_translate_the_frameworks_own_text()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json",
            """{ "PickerClear": "Torles", "PickerPreviousMonth": "Elozo honap" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var code = Generated(run);
        Assert.Contains("IRaskStringSource", code, StringComparison.Ordinal);
        Assert.Contains("RaskString.PickerClear", code, StringComparison.Ordinal);

        // Registered with no AddRask() call, so translating the framework is a file you drop in.
        Assert.Contains("ModuleInitializer", code, StringComparison.Ordinal);
    }

    [Fact]
    public void No_neutral_catalog_is_needed_because_the_frameworks_english_lives_in_the_code()
    {
        // An ordinary family without its neutral catalog is an error; this one is not, and that
        // asymmetry is deliberate — the English defaults are the literals at each call site, which is
        // what makes a missing framework string impossible.
        var run = Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
    }

    [Fact]
    public void A_key_that_is_not_a_framework_string_is_an_error_listing_the_valid_names()
    {
        // Without this an app could translate "PickerClearr" and see nothing change, with nothing to
        // explain why.
        var run = Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClearr": "Torles" }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("not one of the framework's own strings", error.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("PickerClear", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plural_set_in_the_reserved_catalog_is_an_error()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json",
            """{ "PickerClear": { "$plural": "n", "one": "a", "other": "b" } }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains("plain text", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_that_carries_values_numbers_them_so_a_translation_can_reorder_them()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json",
            """{ "PaginationSummary": "Osszesen {2:N0}, ebbol {0}-{1}" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"Osszesen {2:N0}, ebbol {0}-{1}\"", Generated(run), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{0}-{1} / {3}", "a value the text does not carry")]
    [InlineData("{from}-{to} / {total}", "numbers its values")]
    [InlineData("{0}-{1} / {2", "a stray '{'")]
    public void A_translation_the_text_could_not_fill_is_a_build_error(string text, string reason)
    {
        // At runtime it would be passed over for the English, and nobody would know why.
        var run = Run(("/p/Resources/RaskStrings.hu.json", $$"""{ "PaginationSummary": "{{text}}" }"""));

        var error = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK051");
        Assert.Contains(reason, error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_brace_in_a_text_without_values_is_just_a_brace()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles {x}" }"""));

        Assert.Empty(run.RunResult.Diagnostics);
    }

    [Fact]
    public void The_generator_knows_how_many_values_each_documented_text_carries()
    {
        // docs/localization.md lists every key with its English, which is the literal at the call site
        // (the kit's own test holds those two together). The count here has to agree with it, or a
        // correct translation would be refused — or a broken one let through.
        var documented = DocumentedEnglish();

        var wrong = TranslationCatalogGenerator.FrameworkStringKeysForTests
            .Where(key => documented.TryGetValue(key, out var english)
                && Values(english) != TranslationCatalogGenerator.FrameworkStringValuesForTests(key))
            .ToList();

        Assert.NotEmpty(documented);
        Assert.Empty(wrong);
    }

    [Fact]
    public void A_library_registers_its_translations_under_the_apps()
    {
        // Rask.Ui ships its own Hungarian this way. Its root namespace is a TYPE in the kit (Rask.Ui, the
        // `Ui` class in `Rask`), so the source cannot be declared there.
        var run = GeneratorDriverFixture.Run(
            [("App.cs", "namespace Rask { public static class Ui { } }")],
            [new TranslationCatalogGenerator()],
            [("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles" }""")],
            new LibraryOptions("Rask.Ui"));

        var code = Generated(run);

        Assert.Empty(run.RunResult.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("RaskStrings.UseLibrarySource(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Rask.Ui;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_with_no_catalog_is_answered_without_allocating()
    {
        // Every kit text asks on every render, in whatever language the visitor reads: walking en-US down
        // to en by cutting strings would allocate on each of them.
        var code = Generated(Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles" }""")));

        Assert.Contains("AsSpan(cultureTag)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Substring", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_falls_back_to_the_language_the_app_translated()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles" }"""));

        // hu-HU is not a catalog, so the emitted source has to walk to hu before giving up and letting
        // the caller use the framework's English.
        Assert.Contains("LastIndexOf(tag, '-')", Generated(run), StringComparison.Ordinal);
    }

    private static int Values(string english)
    {
        var places = System.Text.RegularExpressions.Regex.Matches(english, @"\{(\d+)");
        return places.Count == 0 ? 0 : places.Max(place => int.Parse(place.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) + 1;
    }

    // The rows of the docs' table: | `Key` | Component | `English` |
    private static Dictionary<string, string> DocumentedEnglish()
    {
        var docs = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "localization.md"));
        return System.Text.RegularExpressions.Regex
            .Matches(docs, @"^\| `(\w+)` \| [^|]+ \| `(.+)` \|$", System.Text.RegularExpressions.RegexOptions.Multiline)
            .ToDictionary(row => row.Groups[1].Value, row => row.Groups[2].Value, StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Rask.slnx was not found above the test output.");
    }

    private sealed class LibraryOptions(string rootNamespace) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; } = new Options(rootNamespace);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options(string rootNamespace) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = key switch
                {
                    "build_property.RootNamespace" => rootNamespace,
                    "build_property.RaskStringsLibrary" => "true",
                    _ => null!,
                };
                return value is not null;
            }
        }
    }
}
