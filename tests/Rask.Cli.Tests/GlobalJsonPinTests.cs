using System.Text.Json;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     The <c>global.json</c> every scaffold writes: which SDK builds a scaffolded app.
/// </summary>
/// <remarks>
///     Without it the SDK picks the newest one installed, so a <c>net10.0</c> app on a machine that also
///     carries the next major in preview is compiled by a release candidate — it builds, says NETSDK1057
///     once per build, and quietly hands two people on the same repository different compilers.
/// </remarks>
public sealed class GlobalJsonPinTests
{
    public static TheoryData<string> Templates() => [.. TemplateCatalog.Keys];

    private static JsonElement Sdk(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("sdk");

    private static ScaffoldFile Pin(string key) =>
        TemplateMaterializer
            .Files("/proj/Shop", key, "Shop", new ServerBatteries(), "9.9.9")
            .Single(f => Path.GetFileName(f.Path) == TemplateMaterializer.GlobalJsonFile);

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_writes_one_at_the_project_root(string key)
    {
        var pin = Pin(key);

        Assert.Equal(
            Path.Combine("/proj/Shop", TemplateMaterializer.GlobalJsonFile),
            pin.Path);

        var sdk = Sdk(pin.Content);
        Assert.Equal("10.0.0", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    /// <summary>
    ///     <c>latestFeature</c>, not <c>latestMajor</c> — the whole point of the file.
    /// </summary>
    /// <remarks>
    ///     <c>latestMajor</c> is the behaviour there already is: it rolls onto the newest SDK on the
    ///     machine whatever its major, which is exactly the preview-SDK selection this exists to stop. A
    ///     pin that permits it is a pin that does nothing.
    /// </remarks>
    [Fact]
    public void The_roll_forward_policy_does_not_cross_a_major()
    {
        var policy = Sdk(Pin("server").Content).GetProperty("rollForward").GetString();

        Assert.Equal("latestFeature", policy);
    }

    /// <summary>
    ///     The pin names the band FLOOR, never a real SDK version.
    /// </summary>
    /// <remarks>
    ///     Measured, not assumed, on a band still in preview: a pin naming the release resolves NOTHING
    ///     while only its release candidate is installed — the candidate sorts below the release, and
    ///     roll-forward only ever goes up. The floor is satisfied by every SDK in the band.
    /// </remarks>
    [Fact]
    public void The_pin_is_a_band_floor_that_a_release_candidate_still_satisfies()
    {
        var version = Sdk(Pin("server").Content).GetProperty("version").GetString();

        Assert.EndsWith(".0.0", DotnetTarget.SdkPin, StringComparison.Ordinal);
        Assert.Equal(DotnetTarget.SdkPin, version);
    }

    [Fact]
    public void The_pin_is_the_only_file_added_beyond_the_committed_tree()
    {
        // The committed trees are written byte for byte, and this file is the one deliberate addition.
        var files = TemplateMaterializer.Files(
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9");

        Assert.Single(files, f => Path.GetFileName(f.Path) == TemplateMaterializer.GlobalJsonFile);
    }
}
