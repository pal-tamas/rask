using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

// Localization: what a scaffolded app starts with, and what lands on disk.
//
// There is no flag surface left to test (#854). The languages an app ships are configured in
// Program.cs, so `rask new` decides one thing only — whether it writes that registration at all —
// and the multi-language cases below drive the generator directly, the way an app does when it
// edits the block.
public class LocalizationScaffoldTests
{
    /// <summary>
    ///     The batteries an app has once it has added languages to the block `rask new` wrote.
    /// </summary>
    /// <remarks>
    ///     Constructed rather than routed through a flag, because there is no flag: this is the state a
    ///     Program.cs edit produces. The generator still has to handle more than one language — that is
    ///     the whole point of the block being editable — so it is still worth testing with two.
    /// </remarks>
    private static ServerBatteries Batteries(params string[] cultures) =>
        new ServerBatteries { Localization = true, CultureList = string.Join(",", cultures) }.Normalized();

    [Fact]
    public void A_localized_template_with_no_languages_named_means_english()
    {
        var batteries = NewCommand.BatteriesOf(["localization"]).Normalized();

        Assert.True(batteries.Localization);
        Assert.Equal(["en"], batteries.Cultures);
    }

    [Fact]
    public void A_bare_new_ships_english_and_the_machinery_to_add_a_second_language()
    {
        // The default is ONE language, and the machinery to add another. Adding Hungarian is a line in
        // the block Program.cs already has, rather than a refactor of every string in the app — which
        // is also why there is no flag for it: the file is where the answer lives.
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

    /// <summary>
    ///     It is not a flag on any template, so it must not be advertised as one.
    /// </summary>
    /// <remarks>
    ///     `SupportedFlags` is printed back to the user verbatim when they pass a `--no-` for something a
    ///     template does not have ("It supports: …"), so leaving `localization` in it would name a flag
    ///     that no longer exists — the accepted-and-disregarded shape, arriving as documentation.
    /// </remarks>
    [Fact]
    public void It_is_advertised_as_a_flag_on_no_template()
    {
        foreach (var template in TemplateCatalog.All)
        {
            Assert.DoesNotContain("localization", template.SupportedFlags);
        }

        Assert.DoesNotContain("localization", NewCommand.FeatureFlags);
        Assert.DoesNotContain("localization", NewCommand.BatteryFlags);
    }

    /// <summary>
    ///     The server scaffolds the registration; browser-WASM does not, and the reason is measurable:
    ///     ICU adds roughly a megabyte of brotli to a published bundle (+32% on the showcase). It is also
    ///     the one part `Program.cs` cannot switch on by itself, since `RaskGlobalization` is an MSBuild
    ///     property — which is why the csproj carries it commented rather than absent.
    /// </summary>
    [Fact]
    public void The_server_ships_it_and_browser_wasm_does_not()
    {
        Assert.True(TemplateCatalog.TryGet("server", out var server));
        Assert.True(server!.ShipsLocalization);
        Assert.True(NewCommand.ToBatteries(server, []).Localization);

        Assert.True(TemplateCatalog.TryGet("wasm", out var wasm));
        Assert.False(wasm!.ShipsLocalization);
        Assert.False(NewCommand.ToBatteries(wasm, []).Localization);
    }

    [Fact]
    public void The_wasm_template_scaffolds_a_catalog_per_language()
    {
        var result = ProjectGenerator.GenerateWasm(
            "/out", "Demo", pwa: false, docker: false, "1.0.0", Batteries("en", "hu"));
        var paths = result.Files.Select(f => f.Path).ToArray();

        Assert.Contains("/out/Resources/Strings.en.json", paths);
        Assert.Contains("/out/Resources/Strings.hu.json", paths);
    }


    /// <summary>
    /// The browser half of the negotiation: <c>host.UseCulture</c> is what tells the runtime which
    /// languages there are to choose between. Without it the catalogs compile and nothing selects them.
    /// </summary>
    [Fact]
    public void The_wasm_program_registers_the_languages()
    {
        var program = WasmProgramOf(Batteries("en", "hu"));

        Assert.Contains("host.UseCulture(", program, StringComparison.Ordinal);
        Assert.Contains("new[] { \"en\", \"hu\" }", program, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wasm_app_that_named_no_language_registers_none()
    {
        Assert.DoesNotContain(
            "UseCulture", WasmProgramOf(NewCommand.BatteriesOf([]).Normalized()), StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap that made this more than "emit the files". A browser runtime built without ICU also has
    /// PredefinedCulturesOnly on, where GetCultureInfo cannot produce anything but the invariant culture —
    /// so Rask's resolver rejects EVERY configured language and the app boots with an empty supported
    /// list and a warning. Catalogs without this property would be the same no-op in a new costume.
    /// </summary>
    [Fact]
    public void Naming_a_language_ships_the_ICU_that_makes_it_resolve()
    {
        Assert.Contains(
            "<RaskGlobalization>true</RaskGlobalization>",
            WasmCsprojOf(Batteries("en", "hu")),
            StringComparison.Ordinal);

        // And an app that named none keeps it commented out, so it costs nothing and is still findable.
        var plain = WasmCsprojOf(NewCommand.BatteriesOf([]).Normalized());
        Assert.DoesNotContain("\n    <RaskGlobalization>true</RaskGlobalization>", plain, StringComparison.Ordinal);
        Assert.Contains("<!-- <RaskGlobalization>true</RaskGlobalization> -->", plain, StringComparison.Ordinal);
    }

    private static string WasmProgramOf(ServerBatteries batteries) =>
        FileOf(batteries, "/out/Program.cs");

    private static string WasmCsprojOf(ServerBatteries batteries) =>
        FileOf(batteries, "/out/Demo.csproj");

    private static string FileOf(ServerBatteries batteries, string path)
    {
        var result = ProjectGenerator.GenerateWasm(
            "/out", "Demo", batteries.Pwa, batteries.Docker, "1.0.0", batteries);

        return result.Files.Single(f => f.Path == path).Content;
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
        // One Add per language: this block is where an app adds its second one, so it reads as a list
        // you extend rather than a loop over an array.
        Assert.Contains("c.SupportedCultures.Add(\"en\");", program, StringComparison.Ordinal);
        Assert.Contains("c.SupportedCultures.Add(\"hu\");", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_language_named_is_the_one_the_app_falls_back_to()
    {
        var program = Program(ProjectGenerator.GenerateServer("/out", "Demo", Batteries("hu", "en"), "1.0.0"));

        Assert.True(
            program.IndexOf("Add(\"hu\")", StringComparison.Ordinal)
            < program.IndexOf("Add(\"en\")", StringComparison.Ordinal),
            "the first language configured must be emitted first — it is the fallback");
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
