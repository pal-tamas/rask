using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

// `rask new --culture hu`: the flag surface, the implications, and what actually lands on disk.
public class LocalizationScaffoldTests
{
    private static ServerBatteries Batteries(params string[] cultures) =>
        NewCommand.BatteriesOf(["localization"], cultures: cultures).Normalized();

    [Fact]
    public void Naming_a_language_is_asking_for_localization()
    {
        var batteries = NewCommand.BatteriesOf([], cultures: ["hu"]).Normalized();

        Assert.True(batteries.Localization);
        Assert.Equal(["hu"], batteries.Cultures);
    }

    [Fact]
    public void Asking_for_localization_without_naming_one_means_english()
    {
        var batteries = NewCommand.BatteriesOf(["localization"]).Normalized();

        Assert.True(batteries.Localization);
        Assert.Equal(["en"], batteries.Cultures);
    }

    [Fact]
    public void A_bare_new_ships_english_and_the_machinery_to_add_a_second_language()
    {
        // This used to assert the opposite, on the grounds that shipping a second language is a
        // commitment a convenience flag shouldn't make. It still is — which is why the default is ONE
        // language. What comes on by default is the machinery, so adding Hungarian later is a --culture
        // rather than a refactor of every string in the app.
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, []);

        Assert.True(batteries.Localization);
        Assert.Equal(["en"], batteries.Cultures);
    }

    [Fact]
    public void Two_batteries_with_the_same_languages_compare_equal()
    {
        // The reason CultureList is a string rather than a list. ServerBatteries is a record, and a
        // collection property silently degrades its synthesized value equality to reference equality —
        // which would break the value comparisons elsewhere in these tests, in some later change that
        // looked unrelated.
        Assert.Equal(Batteries("en", "hu"), Batteries("en", "hu"));
        Assert.NotEqual(Batteries("en", "hu"), Batteries("en", "de"));
    }

    [Fact]
    public void Only_a_template_that_scaffolds_a_catalog_advertises_it()
    {
        // This used to assert the opposite — that all three web templates support localization — which
        // pinned a bug rather than a behaviour: neither WASM generator reads Localization or Cultures, so
        // both accepted the flag and scaffolded nothing. Asserting PRESENCE is how that survived.
        Assert.True(TemplateCatalog.TryGet("server", out var server));
        Assert.Contains("localization", server!.SupportedFlags);

        foreach (var key in new[] { "wasm", "wasm-hosted" })
        {
            Assert.True(TemplateCatalog.TryGet(key, out var template));
            Assert.DoesNotContain("localization", template!.SupportedFlags);
        }
    }

    /// <summary>The negative control for the guard above: the WASM generators really do ignore it.</summary>
    /// <remarks>
    /// Delete this test when https://github.com/pal-tamas/rask/issues/846 lands — at which point the
    /// templates get the flag back and the guard above flips with it.
    /// </remarks>
    [Fact]
    public void The_wasm_generators_still_ignore_a_language_they_are_handed()
    {
        var result = ProjectGenerator.GenerateWasm(
            "/out", "Demo", auth: false, pwa: false, docker: false, "1.0.0", bootstrap: false,
            Batteries("en", "hu"));

        Assert.DoesNotContain(result.Files, f => f.Path.Contains("Resources/Strings", StringComparison.Ordinal));
    }

    [Fact]
    public void One_catalog_is_scaffolded_per_language()
    {
        var result = ProjectGenerator.GenerateServer("/out", "Demo", Batteries("en", "hu"), "1.0.0");
        var paths = result.Files.Select(f => f.Path).ToArray();

        Assert.Contains("/out/Resources/Strings.en.json", paths);
        Assert.Contains("/out/Resources/Strings.hu.json", paths);
    }

    [Fact]
    public void An_app_that_did_not_ask_for_it_gets_no_catalogs_and_no_culture_config()
    {
        var result = ProjectGenerator.GenerateServer(
            "/out", "Demo", NewCommand.BatteriesOf([]).Normalized(), "1.0.0");

        Assert.DoesNotContain(result.Files, f => f.Path.Contains("Resources/Strings", StringComparison.Ordinal));
        Assert.DoesNotContain("configureCulture", Program(result), StringComparison.Ordinal);
    }

    [Fact]
    public void The_languages_are_registered_on_the_one_AddRask_call()
    {
        // The bug this pins: emitting a SECOND AddRask(configureCulture: ...) compiles and reads
        // correctly, but the options register with TryAddSingleton — so the first, empty registration
        // wins and the app ships with no languages at all while looking entirely right.
        var program = Program(ProjectGenerator.GenerateServer("/out", "Demo", Batteries("en", "hu"), "1.0.0"));

        Assert.Equal(1, CountOf(program, "builder.Services.AddRask("));
        Assert.Contains("configureCulture", program, StringComparison.Ordinal);
        Assert.Contains("\"en\", \"hu\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_language_named_is_the_one_the_app_falls_back_to()
    {
        var program = Program(ProjectGenerator.GenerateServer("/out", "Demo", Batteries("hu", "en"), "1.0.0"));

        Assert.Contains("\"hu\", \"en\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_neutral_catalog_defines_the_keys_and_a_translation_starts_from_it()
    {
        var result = ProjectGenerator.GenerateServer("/out", "Demo", Batteries("en", "hu"), "1.0.0");

        var neutral = File(result, "/out/Resources/Strings.en.json");
        var translation = File(result, "/out/Resources/Strings.hu.json");

        // Same keys, so the build immediately reports what still needs translating (RASK052) rather
        // than the app silently rendering English where a key was forgotten.
        foreach (var key in new[] { "AppTitle", "Greeting", "Items" })
        {
            Assert.Contains($"\"{key}\"", neutral, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", translation, StringComparison.Ordinal);
        }

        // And it shows the plural shape, which is the part nobody guesses.
        Assert.Contains("$plural", neutral, StringComparison.Ordinal);
    }

    private static string Program(ScaffoldResult result) => File(result, "/out/Program.cs");

    private static string File(ScaffoldResult result, string path) =>
        result.Files.Single(f => f.Path == path).Content;

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
