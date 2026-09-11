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
