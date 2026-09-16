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

    private static ScaffoldFile Pin(string key, DotnetTarget dotnet) =>
        TemplateMaterializer
            .Files("/proj/Shop", key, "Shop", new ServerBatteries(), "9.9.9", dotnet)
            .Single(f => Path.GetFileName(f.Path) == TemplateMaterializer.GlobalJsonFile);

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_writes_one_at_the_project_root(string key)
    {
        var pin = Pin(key, DotnetTarget.Default);

        Assert.Equal(
            Path.Combine("/proj/Shop", TemplateMaterializer.GlobalJsonFile),
            pin.Path);

        var sdk = Sdk(pin.Content);
        Assert.Equal("10.0.0", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void The_pin_follows_the_framework_that_was_asked_for()
    {
        Assert.Equal("11.0.0", Sdk(Pin("server", DotnetTarget.Preview).Content)
            .GetProperty("version").GetString());
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
        foreach (var dotnet in new[] { DotnetTarget.Default, DotnetTarget.Preview })
        {
            Assert.Equal("latestFeature", Sdk(Pin("server", dotnet).Content)
                .GetProperty("rollForward").GetString());
        }
    }

    /// <summary>
    ///     The pin names the band FLOOR, <c>{major}.0.0</c>, never a real SDK version.
    /// </summary>
    /// <remarks>
    ///     Measured, not assumed. With only <c>11.0.100-rc.1</c> installed, a pin of <c>11.0.100</c>
    ///     resolves NOTHING — the release candidate sorts below the release it is a candidate for, and
    ///     roll-forward only ever goes up, so the SDK exits with "the command could not be loaded" before
    ///     a line of the scaffold is compiled. <c>allowPrerelease</c> does not rescue it. The floor is
    ///     satisfied by every SDK in the band including prereleases, and it does not go stale at GA.
    /// </remarks>
    [Fact]
    public void The_pin_is_a_band_floor_that_a_release_candidate_still_satisfies()
    {
        foreach (var dotnet in new[] { DotnetTarget.Default, DotnetTarget.Preview })
        {
            Assert.Equal($"{dotnet.SdkMajor}.0.0", dotnet.SdkPin);
            Assert.EndsWith(".0.0", Sdk(Pin("server", dotnet).Content)
                .GetProperty("version").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A template tree that ever commits a <c>global.json</c> of its own is caught, not trusted.
    /// </summary>
    /// <remarks>
    ///     The generated pin replaces such a file, so this can only bite if the generation is removed.
    ///     It is guarded anyway because the failure is silent in the direction that matters: a
    ///     <c>--framework net11.0</c> scaffold pinned to the 10.0 band restores nothing, and says so in
    ///     an error that names neither the framework nor the file.
    /// </remarks>
    [Fact]
    public void A_committed_pin_naming_the_default_band_is_caught_and_rewritten()
    {
        var committed = DotnetTarget.Default.GlobalJson;

        Assert.True(DotnetTarget.Preview.StillNamesTheDefault(committed));
        Assert.False(
            DotnetTarget.Preview.StillNamesTheDefault(DotnetTarget.Preview.Rewrite(committed)));
        Assert.Equal(
            "11.0.0",
            Sdk(DotnetTarget.Preview.Rewrite(committed)).GetProperty("version").GetString());
    }

    [Fact]
    public void The_pin_is_the_only_file_the_default_target_adds_beyond_the_committed_tree()
    {
        // The default target still writes the committed trees byte for byte -- DotnetTarget.Rewrite is a
        // no-op there, and this file is the one deliberate addition on top of them.
        var files = TemplateMaterializer.Files(
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9", DotnetTarget.Default);

        Assert.Single(files, f => Path.GetFileName(f.Path) == TemplateMaterializer.GlobalJsonFile);
    }
}
