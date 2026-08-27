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
    public void Every_web_template_advertises_it_now()
    {
        // Between #849 and #846 this asserted the opposite for the WASM pair, because they took the flag
        // and scaffolded nothing — struck off rather than left as a silent no-op. They emit catalogs, the
        // negotiation and the ICU now, so the flag means the same thing wherever it is listed.
        foreach (var key in new[] { "server", "wasm", "wasm-hosted" })
        {
            Assert.True(TemplateCatalog.TryGet(key, out var template));
            Assert.Contains("localization", template!.SupportedFlags);
        }
    }

    /// <summary>
    /// On the browser templates it is supported but not standard, and the reason is measurable: ICU adds
    /// roughly a megabyte of brotli to a published bundle (+32% on the showcase). A battery is wiring you
    /// would otherwise write by hand; a third more download is an opinion about the app.
    /// </summary>
    [Fact]
    public void The_browser_templates_support_it_without_including_it()
    {
        Assert.True(TemplateCatalog.TryGet("server", out var server));
        Assert.Empty(server!.OptInFlags);
        Assert.True(NewCommand.ToBatteries(server, []).Localization);

        foreach (var key in new[] { "wasm", "wasm-hosted" })
        {
            Assert.True(TemplateCatalog.TryGet(key, out var template));
            Assert.Equal(["localization"], template!.OptInFlags);

            // Supported, so --culture works; not standard, so a bare `rask new` does not pay for it.
            Assert.False(NewCommand.ToBatteries(template, []).Localization);
            Assert.True(NewCommand.ToBatteries(template, [], cultures: ["hu"]).Localization);
        }
    }

    /// <summary>Nothing may be named opt-in that the template does not support in the first place.</summary>
    [Fact]
    public void An_opt_in_battery_is_one_the_template_actually_has()
    {
        foreach (var template in TemplateCatalog.All)
        {
            Assert.All(template.OptInFlags, flag => Assert.Contains(flag, template.SupportedFlags));
        }
    }

    [Fact]
    public void The_wasm_template_scaffolds_a_catalog_per_language()
    {
        var result = ProjectGenerator.GenerateWasm(
            "/out", "Demo", auth: false, pwa: false, docker: false, "1.0.0", Batteries("en", "hu"));
        var paths = result.Files.Select(f => f.Path).ToArray();

        Assert.Contains("/out/Resources/Strings.en.json", paths);
        Assert.Contains("/out/Resources/Strings.hu.json", paths);
    }

    [Fact]
    public void The_wasm_hosted_catalogs_live_in_the_client_that_renders()
    {
        // Not the .Server: it is a static-file host for the baked bundle, renders nothing, and has no
        // text to translate. Putting them there would compile and never be read.
        var result = ProjectGenerator.GenerateWasmHosted("/out", "Demo", Batteries("en", "hu"), "1.0.0");
        var paths = result.Files.Select(f => f.Path).ToArray();

        Assert.Contains("/out/Demo.Client/Resources/Strings.en.json", paths);
        Assert.Contains("/out/Demo.Client/Resources/Strings.hu.json", paths);
        Assert.DoesNotContain(paths, p => p.Contains("Demo.Server/Resources", StringComparison.Ordinal));
    }

    /// <summary>
    /// The browser half of the negotiation: <c>host.UseCulture</c> is what tells the runtime which
    /// languages there are to choose between. Without it the catalogs compile and nothing selects them.
    /// </summary>
    [Theory]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void The_wasm_program_registers_the_languages(string key)
    {
        var program = WasmProgramOf(key, Batteries("en", "hu"));

        Assert.Contains("host.UseCulture(", program, StringComparison.Ordinal);
        Assert.Contains("new[] { \"en\", \"hu\" }", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void A_wasm_app_that_named_no_language_registers_none(string key)
    {
        Assert.DoesNotContain(
            "UseCulture", WasmProgramOf(key, NewCommand.BatteriesOf([]).Normalized()), StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap that made this more than "emit the files". A browser runtime built without ICU also has
    /// PredefinedCulturesOnly on, where GetCultureInfo cannot produce anything but the invariant culture —
    /// so Rask's resolver rejects EVERY configured language and the app boots with an empty supported
    /// list and a warning. Catalogs without this property would be the same no-op in a new costume.
    /// </summary>
    [Theory]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void Naming_a_language_ships_the_ICU_that_makes_it_resolve(string key)
    {
        Assert.Contains(
            "<RaskGlobalization>true</RaskGlobalization>",
            WasmCsprojOf(key, Batteries("en", "hu")),
            StringComparison.Ordinal);

        // And an app that named none keeps it commented out, so it costs nothing and is still findable.
        var plain = WasmCsprojOf(key, NewCommand.BatteriesOf([]).Normalized());
        Assert.DoesNotContain("\n    <RaskGlobalization>true</RaskGlobalization>", plain, StringComparison.Ordinal);
        Assert.Contains("<!-- <RaskGlobalization>true</RaskGlobalization> -->", plain, StringComparison.Ordinal);
    }

    private static string WasmProgramOf(string key, ServerBatteries batteries) =>
        FileOf(key, batteries, key == "wasm" ? "/out/Program.cs" : "/out/Demo.Client/Program.cs");

    private static string WasmCsprojOf(string key, ServerBatteries batteries) =>
        FileOf(key, batteries, key == "wasm" ? "/out/Demo.csproj" : "/out/Demo.Client/Demo.Client.csproj");

    private static string FileOf(string key, ServerBatteries batteries, string path)
    {
        var result = key == "wasm"
            ? ProjectGenerator.GenerateWasm(
                "/out", "Demo", batteries.Auth, batteries.Pwa, batteries.Docker, "1.0.0", bootstrap: false, batteries)
            : ProjectGenerator.GenerateWasmHosted("/out", "Demo", batteries, "1.0.0");

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
