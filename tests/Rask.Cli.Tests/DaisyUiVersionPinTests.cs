using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Cli.Tests;

/// <summary>
///     A front-end template installs the daisyUI the UI kit vendors.
/// </summary>
/// <remarks>
///     One library, two deliveries: a C# host compiles the bundle <c>Rask.Ui</c> vendors, a front end installs
///     <c>daisyui</c> from npm. The npm <b>range must accept the vendored version</b> — <c>^5.7.0</c> and
///     <c>5.7.27</c> agree, <c>^5.7.0</c> and <c>6.0.1</c> do not — or the same starter page renders differently
///     depending on which toolchain built its stylesheet. It goes when the kit stops vendoring daisyUI.
/// </remarks>
public sealed class DaisyUiVersionPinTests
{
    [Fact]
    public void The_npm_range_accepts_the_version_the_kit_vendors()
    {
        var vendored = Version.Parse(Regex.Match(
            RepoPins.Text("src/Rask.Ui/Styles/daisyui.mjs"), @"var version\s*=\s*""([0-9]+\.[0-9]+\.[0-9]+)""").Groups[1].Value);

        var ranges = TailwindVersionPinTests.TemplateManifests("daisyui");

        Assert.NotEmpty(ranges);
        Assert.All(ranges, range =>
        {
            var floor = Regex.Match(range.Value, @"^\^([0-9]+\.[0-9]+\.[0-9]+)$");
            Assert.True(floor.Success, $"{range.Key}: expected a caret range like ^5.7.0, got '{range.Value}'.");
            var lowest = Version.Parse(floor.Groups[1].Value);
            Assert.True(
                vendored.Major == lowest.Major && vendored >= lowest,
                string.Create(CultureInfo.InvariantCulture, $"{range.Key} installs daisyUI '{range.Value}', which does not accept the {vendored} Rask.Ui vendors."));
        });
    }
}
