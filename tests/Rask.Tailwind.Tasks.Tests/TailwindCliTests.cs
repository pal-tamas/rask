using System.Runtime.InteropServices;
using Rask.Tailwind.Tasks;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     Which Tailwind binary a machine gets, and whether a download can be trusted.
/// </summary>
/// <remarks>
///     The platform mapping is the part with judgement in it: a wrong asset name is a 404 several
///     minutes into somebody's first build, and a missing one is the difference between falling back to
///     npm and failing outright.
/// </remarks>
public class TailwindCliTests
{
    [Theory]
    [InlineData(TailwindOs.MacOs, Architecture.Arm64, false, "tailwindcss-macos-arm64")]
    [InlineData(TailwindOs.MacOs, Architecture.X64, false, "tailwindcss-macos-x64")]
    [InlineData(TailwindOs.Linux, Architecture.X64, false, "tailwindcss-linux-x64")]
    [InlineData(TailwindOs.Linux, Architecture.Arm64, false, "tailwindcss-linux-arm64")]
    public void Each_published_platform_maps_to_its_asset(
        TailwindOs os, Architecture architecture, bool musl, string expected) =>
        Assert.Equal(expected, TailwindCli.AssetName(os, architecture, musl));

    [Theory]
    [InlineData(Architecture.X64, "tailwindcss-linux-x64-musl")]
    [InlineData(Architecture.Arm64, "tailwindcss-linux-arm64-musl")]
    public void Alpine_gets_the_musl_build(Architecture architecture, string expected)
    {
        // The glibc binary does not run on musl, and the failure is a bare "not found" from the loader
        // that names neither Tailwind nor libc — so getting this wrong is expensive to diagnose.
        Assert.Equal(expected, TailwindCli.AssetName(TailwindOs.Linux, architecture, isMusl: true));
    }

    [Fact]
    public void Windows_on_ARM_takes_the_x64_binary()
    {
        // Tailwind publishes no windows-arm64 asset. Windows emulates x64 transparently, so falling back
        // gives a working build where refusing would fail on a machine that is perfectly capable — and
        // RaskTailwindEngine=npm gets a native engine for anyone who minds the emulation.
        Assert.Equal("tailwindcss-windows-x64.exe", TailwindCli.AssetName(TailwindOs.Windows, Architecture.Arm64, false));
        Assert.Equal("tailwindcss-windows-x64.exe", TailwindCli.AssetName(TailwindOs.Windows, Architecture.X64, false));
    }

    [Theory]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.X86)]
    public void An_architecture_with_no_build_asks_for_nothing(Architecture architecture)
    {
        // 32-bit ARM — a Raspberry Pi — has no standalone build at all. Returning null is what routes it
        // to the npm engine, which does have one; inventing an asset name would 404 instead.
        Assert.Null(TailwindCli.AssetName(TailwindOs.Linux, architecture, isMusl: false));
    }

    [Fact]
    public void The_checksum_manifest_is_read_in_the_format_it_is_actually_published_in()
    {
        // The names carry a leading "./" — part of the sha256sum format rather than the filename. Missing
        // it made every download "fail verification" and silently fall through to npm, which looked like
        // a network problem and was not.
        const string Manifest = """
            55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195  ./tailwindcss-linux-arm64
            cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d  ./tailwindcss-macos-arm64
            """;

        Assert.Equal(
            "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d",
            TailwindCli.ExpectedChecksum(Manifest, "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void A_binary_mode_star_is_not_part_of_the_name_either()
    {
        const string Manifest =
            "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d  *tailwindcss-macos-arm64";

        Assert.NotNull(TailwindCli.ExpectedChecksum(Manifest, "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void An_asset_the_manifest_does_not_mention_has_no_checksum()
    {
        // Reported as unverifiable rather than treated as a pass. This file is about to be executed by
        // the build, so "no entry" must never mean "nothing to check".
        const string Manifest =
            "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d  ./tailwindcss-linux-x64";

        Assert.Null(TailwindCli.ExpectedChecksum(Manifest, "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void A_truncated_hash_is_refused()
    {
        Assert.Null(TailwindCli.ExpectedChecksum("abc123  ./tailwindcss-macos-arm64", "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void The_cache_is_keyed_by_version()
    {
        // So two projects on different Tailwind versions coexist, and so an upgrade is a fresh download
        // rather than overwriting a file another build may be executing.
        Assert.NotEqual(
            TailwindCli.CachePath("/c", "4.3.3", "tailwindcss-macos-arm64"),
            TailwindCli.CachePath("/c", "4.4.0", "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void The_download_url_pins_the_version()
    {
        // Never `latest`. Tailwind is a compiler: a different version emits different CSS, so a floating
        // one would change what an app looks like with no diff to point at.
        Assert.Equal(
            "https://github.com/tailwindlabs/tailwindcss/releases/download/v4.3.3/tailwindcss-macos-arm64",
            TailwindCli.DownloadUrl("4.3.3", "tailwindcss-macos-arm64"));
    }

    [Fact]
    public void The_cache_lives_outside_the_repository()
    {
        // An 80 MB binary in obj/ is re-downloaded by every clean and committed by somebody eventually.
        var root = TailwindCli.DefaultCacheRoot("/home/ada");

        Assert.StartsWith("/home/ada", root, StringComparison.Ordinal);
        Assert.Contains("tailwind", root, StringComparison.Ordinal);
    }
}
