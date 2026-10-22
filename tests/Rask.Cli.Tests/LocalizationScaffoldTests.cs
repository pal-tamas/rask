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
        var batteries = BatterySelection.BatteriesOf(["localization"]).Normalized();

        Assert.True(batteries.Localization);
        Assert.Equal(["en"], batteries.Cultures);
    }

    [Fact]
    public void A_bare_new_ships_english_and_the_machinery_to_add_a_second_language()
    {
        // The default is ONE language, and the machinery to add another. Adding Hungarian is a line in
        // the block Program.cs already has, rather than a refactor of every string in the app — which
        // is also why there is no flag for it: the file is where the answer lives.
        var batteries = BatterySelection.ToBatteries(TemplateCatalog.Default, []);

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

        Assert.DoesNotContain("localization", BatterySelection.FeatureFlags);
        Assert.DoesNotContain("localization", BatterySelection.BatteryFlags);
    }

    /// <summary>
    ///     Every C# template starts with English: the server, the browser app and the hosted one alike.
    /// </summary>
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void Every_csharp_template_ships_it(string key)
    {
        Assert.True(TemplateCatalog.TryGet(key, out var template));

        var batteries = BatterySelection.ToBatteries(template!, []);

        Assert.True(template!.ShipsLocalization);
        Assert.True(batteries.Localization);
    }

    /// <summary>
    ///     A browser app lists its languages where a server app does — <c>Rask:Cultures</c> — in the file its
    ///     build reads, and starts with English, which ships no ICU. The csproj no longer carries the switch: the
    ///     build turns ICU on when the list names another language.
    /// </summary>
    [Fact]
    public void A_browser_app_lists_english_in_its_settings_and_ships_its_catalog()
    {
        var result = ProjectGenerator.GenerateWasm(
            "/out", "Demo", pwa: false, docker: false, "1.0.0", BatterySelection.ToBatteries(Wasm(), []));

        Assert.Contains("\"Cultures\": [ \"en\" ]", File(result, "/out/wwwroot/appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"AppTitle\"", File(result, "/out/Resources/Strings.en.json"), StringComparison.Ordinal);
        Assert.DoesNotContain("<RaskGlobalization>true", File(result, "/out/Demo.csproj"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A hosted app names its languages once, in the host's file, which the browser half's build reads — and
    ///     its catalog sits in Client/, the half that renders the pages.
    /// </summary>
    [Fact]
    public void A_hosted_app_lists_english_in_the_hosts_settings_and_ships_its_catalog_in_the_client()
    {
        Assert.True(TemplateCatalog.TryGet("wasm-hosted", out var hosted));

        var result = ProjectGenerator.GenerateWasmHosted("/out", "Demo", BatterySelection.ToBatteries(hosted!, []), "1.0.0");

        Assert.Contains("\"Cultures\": [ \"en\" ]", File(result, "/out/appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"AppTitle\"", File(result, "/out/Client/Resources/Strings.en.json"), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Files, f => f.Path == "/out/Resources/Strings.en.json");
    }

    private static TemplateInfo Wasm()
    {
        Assert.True(TemplateCatalog.TryGet("wasm", out var wasm));
        return wasm!;
    }

    private static string File(ScaffoldResult result, string path) =>
        result.Files.Single(f => f.Path == path).Content;
}
