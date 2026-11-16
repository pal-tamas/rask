using System.Text.Json;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     The <c>global.json</c> every scaffold writes: which SDK builds a scaffolded app.
/// </summary>
/// <remarks>
///     Without it the SDK picks the newest one installed, so a <c>net11.0</c> app on a machine that also
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
        Assert.Equal("11.0.100", sdk.GetProperty("version").GetString());
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
    ///     The pin is the band's first RELEASED SDK: every release from it on satisfies it, and no release candidate
    ///     does.
    /// </summary>
    /// <remarks>
    ///     The .NET 11 candidates carry the WebAssembly debug proxy CVE-2026-102870 was fixed in, and a Debug build
    ///     of a browser app starts that proxy. The SDK orders versions by SemVer precedence, where a candidate sorts
    ///     BELOW the release it is a candidate for, and <c>latestFeature</c> only rolls up within the pin's
    ///     major.minor — the two rules <see cref="Satisfies" /> applies.
    /// </remarks>
    [Theory]
    [InlineData("11.0.100", true)]
    [InlineData("11.0.101", true)]
    [InlineData("11.0.200", true)]
    [InlineData("11.0.300-preview.1.27012.3", true)]
    [InlineData("11.0.100-rc.1.26425.128", false)]
    [InlineData("11.0.100-rc.2.26475.104", false)]
    [InlineData("10.0.400", false)]
    [InlineData("12.0.100", false)]
    public void The_pin_is_the_first_release_of_the_band_which_no_release_candidate_satisfies(string installed, bool satisfies)
    {
        var pin = Sdk(Pin("server").Content).GetProperty("version").GetString()!;

        var answer = Satisfies(installed, pin);

        Assert.Equal(DotnetTarget.SdkPin, pin);
        Assert.Equal(satisfies, answer);
    }

    private static bool Satisfies(string installed, string pin)
    {
        var (have, haveCandidate) = Split(installed);
        var (want, wantCandidate) = Split(pin);
        if (have.Major != want.Major || have.Minor != want.Minor)
        {
            return false;
        }

        var core = have.CompareTo(want);
        return core > 0 || (core == 0 && (!haveCandidate || wantCandidate));
    }

    private static (Version Core, bool Prerelease) Split(string version)
    {
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (Version.Parse(version), false) : (Version.Parse(version[..dash]), true);
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
