using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The two daisyUI pins must agree.
/// </summary>
/// <remarks>
///     <para>
///         daisyUI reaches a scaffolded project two ways, because the two Tailwind engines resolve a
///         plugin differently. A front end has node and a package tree, so it installs
///         <c>daisyui</c> from npm and says <c>@plugin "daisyui"</c>. A C# host compiles with the
///         standalone binary, which carries no package tree at all, so it loads the bundle
///         <c>Rask.Ui</c> vendors by relative path. One library, two delivery mechanisms.
///     </para>
///     <para>
///         Drift between them is quiet: the same starter page, scaffolded on a front end and on a C#
///         host, would compile against different daisyUI versions and render differently — which is
///         precisely the thing every template drawing the same skeleton is meant to rule out. And
///         nothing else watches it: <c>dependabot.yml</c> declares no npm ecosystem, and the vendored
///         bundle is a file rather than a version string.
///     </para>
///     <para>
///         The check is that the npm <b>range accepts the vendored version</b>, not that the strings
///         match — they are deliberately different shapes. <c>^5.7.0</c> and <c>5.7.27</c> agree;
///         <c>^5.7.0</c> and <c>6.0.1</c> do not.
///     </para>
/// </remarks>
public sealed class DaisyUiVersionPinTests
{
    [Fact]
    public void The_npm_range_accepts_the_version_the_kit_vendors()
    {
        var vendored = VendoredVersion();
        var range = Regex.Match(SpaDaisyUiRange, @"^\^([0-9]+)\.([0-9]+)\.([0-9]+)$").Groups;

        Assert.True(range.Count == 4, $"Expected a caret range like ^5.7.0, got '{SpaDaisyUiRange}'.");

        int RangePart(int i) => int.Parse(range[i].Value);

        Assert.True(
            vendored.Major == RangePart(1),
            $"Rask.Ui vendors daisyUI {vendored.Major}.x while the front-end templates install "
            + $"'{SpaDaisyUiRange}'. Different majors rename and drop classes, so the same starter page "
            + "would render differently depending on which half of the toolchain built its stylesheet.");

        var acceptsVendored =
            vendored.Minor > RangePart(2)
            || (vendored.Minor == RangePart(2) && vendored.Patch >= RangePart(3));

        Assert.True(
            acceptsVendored,
            $"'{SpaDaisyUiRange}' does not accept {vendored.Major}.{vendored.Minor}.{vendored.Patch}, the "
            + "version Rask.Ui vendors, so a front end would install an older daisyUI than a C# host "
            + "compiles. Raise the range floor with the bundle.");
    }

    [Fact]
    public void The_bundle_and_the_comment_beside_it_say_the_same_thing()
    {
        // The version lives in `var version` inside the bundle and is restated in ui.css's @plugin
        // comment, so a bump has to touch both — and a bump that touches only the file leaves the
        // comment lying about what is shipping.
        var vendored = VendoredVersion();
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "ui.css"));

        Assert.Contains(
            $"daisyUI {vendored.Major}.{vendored.Minor}.{vendored.Patch}",
            css,
            StringComparison.Ordinal);
    }

    /// <summary>The version inside the vendored bundle, which is the one a C# host actually compiles.</summary>
    private static (int Major, int Minor, int Patch) VendoredVersion()
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "vendor", "daisyui.mjs");
        Assert.True(File.Exists(bundle), $"the vendored daisyUI bundle moved: {bundle}");

        var match = Regex.Match(File.ReadAllText(bundle), @"var version\s*=\s*""([0-9]+)\.([0-9]+)\.([0-9]+)""");

        Assert.True(
            match.Success,
            "could not read `var version` out of the vendored daisyUI bundle. That string is where the "
            + "version actually lives; if the bundle's shape changed, this test needs to change with it "
            + "rather than be dropped.");

        return (
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
    }

    /// <summary>Reads the range the SPA generator writes, via the package.json patch that carries it.</summary>
    private static string SpaDaisyUiRange
    {
        get
        {
            var result = ProjectGenerator.GenerateSpa(
                "/proj/App", "App", SpaFramework.React, new ServerBatteries(), "9.9.9");

            var patch = result.Patches.Single(p => p.Path.EndsWith("package.json", StringComparison.Ordinal));
            var json = patch.Transform("""{ "dependencies": {}, "devDependencies": {}, "scripts": {} }""");

            return Regex.Match(json, @"""daisyui""\s*:\s*""([^""]+)""").Groups[1].Value;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
