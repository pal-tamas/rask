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
    public void A_region_falls_back_to_the_language_the_app_translated()
    {
        var run = Run(("/p/Resources/RaskStrings.hu.json", """{ "PickerClear": "Torles" }"""));

        // hu-HU is not a catalog, so the emitted source has to walk to hu before giving up and letting
        // the caller use the framework's English.
        Assert.Contains("LastIndexOf('-')", Generated(run), StringComparison.Ordinal);
    }
}
