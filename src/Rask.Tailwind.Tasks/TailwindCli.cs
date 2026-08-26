using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Rask.Tailwind.Tasks;

/// <summary>The platforms Tailwind publishes a standalone binary for.</summary>
/// <remarks>
///     Public only so the tests can name it in a theory. This assembly is build-only — it is loaded by
///     a UsingTask and never referenced by a consumer — so there is no API surface to protect.
/// </remarks>
public enum TailwindOs
{
    MacOs,
    Linux,
    Windows,
}

/// <summary>
///     Which Tailwind binary this machine needs, and where it is cached.
/// </summary>
/// <remarks>
///     Pure, and separated from the task that downloads it, because the platform mapping is the part
///     with judgement in it — a wrong asset name is a 404 several minutes into someone's first build.
/// </remarks>
internal static class TailwindCli
{
    /// <summary>
    ///     The release asset for a platform, or null when Tailwind publishes none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Windows on ARM64 deliberately resolves to the <b>x64</b> binary. Tailwind publishes no
    ///         windows-arm64 asset, and Windows emulates x64 transparently — so falling back runs, where
    ///         refusing would fail a build on a machine that is perfectly capable of it.
    ///     </para>
    ///     <para>
    ///         Linux on musl needs its own build: the glibc binary does not run on Alpine, and the failure
    ///         is a bare "not found" from the loader rather than anything that names the real problem.
    ///     </para>
    /// </remarks>
    public static string? AssetName(TailwindOs os, Architecture architecture, bool isMusl)
    {
        // Tailwind publishes x64 and arm64 only. A 32-bit ARM box (a Raspberry Pi) has no standalone
        // build at all — npm does, so returning null here is what routes it to the fallback rather than
        // handing it a binary it cannot execute.
        var isArm64 = architecture == Architecture.Arm64;
        if (architecture is not (Architecture.X64 or Architecture.Arm64))
        {
            return null;
        }

        return os switch
        {
            TailwindOs.MacOs => isArm64 ? "tailwindcss-macos-arm64" : "tailwindcss-macos-x64",
            TailwindOs.Linux when isMusl => isArm64
                ? "tailwindcss-linux-arm64-musl"
                : "tailwindcss-linux-x64-musl",
            TailwindOs.Linux => isArm64 ? "tailwindcss-linux-arm64" : "tailwindcss-linux-x64",

            // No windows-arm64 asset exists. x64 runs under emulation on Windows on ARM, which is a
            // working build rather than a refused one — and npm's native win32-arm64 engine is one
            // RaskTailwindEngine=npm away if the emulation cost matters.
            TailwindOs.Windows => "tailwindcss-windows-x64.exe",
            _ => null,
        };
    }

    /// <summary>Where a given version's binary lives once fetched.</summary>
    /// <remarks>
    ///     Keyed by version so two projects on different Tailwind versions coexist, and so upgrading is a
    ///     fresh download rather than an overwrite of a file another build may be executing.
    /// </remarks>
    public static string CachePath(string cacheRoot, string version, string assetName) =>
        Path.Combine(cacheRoot, version, assetName);

    /// <summary>The release URL for a pinned version.</summary>
    public static string DownloadUrl(string version, string assetName) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "https://github.com/tailwindlabs/tailwindcss/releases/download/v{0}/{1}",
            version,
            assetName);

    /// <summary>The checksum manifest published beside the binaries.</summary>
    public static string ChecksumUrl(string version) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "https://github.com/tailwindlabs/tailwindcss/releases/download/v{0}/sha256sums.txt",
            version);

    /// <summary>
    ///     The expected SHA-256 for one asset, read from the release's <c>sha256sums.txt</c>.
    /// </summary>
    /// <remarks>
    ///     Verified rather than trusted. This downloads an executable and then runs it during a build, so
    ///     the one cheap thing that can be checked is checked. Returns null when the manifest does not
    ///     mention the asset, which the caller reports rather than treating as a pass.
    /// </remarks>
    public static string? ExpectedChecksum(string manifest, string assetName)
    {
        if (manifest is null)
        {
            return null;
        }

        foreach (var line in manifest.Split('\n'))
        {
            // "<64 hex>  <name>", the sha256sum format.
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var separator = trimmed.LastIndexOf(' ');
            if (separator < 0)
            {
                continue;
            }

            // "<hash>  ./tailwindcss-macos-arm64" — the names carry a leading ./ (and, in binary mode,
            // a leading *). Both are part of the sha256sum format rather than the filename, and missing
            // them means every download "fails verification" and silently falls through to the npm path.
            var name = trimmed.Substring(separator + 1).Trim('*', ' ');
            if (name.StartsWith("./", StringComparison.Ordinal))
            {
                name = name.Substring(2);
            }

            if (!string.Equals(name, assetName, StringComparison.Ordinal))
            {
                continue;
            }

            var hash = trimmed.Substring(0, separator).Trim();
            return hash.Length == 64 ? hash : null;
        }

        return null;
    }

    /// <summary>
    ///     The default cache root: beside the user's other Rask tooling, not inside the project.
    /// </summary>
    /// <remarks>
    ///     Outside the repository on purpose. An 80 MB binary in <c>obj/</c> is re-downloaded by every
    ///     clean and committed by somebody eventually; one per user, shared by every project, is paid for
    ///     once.
    /// </remarks>
    public static string DefaultCacheRoot(string homeDirectory) =>
        Path.Combine(homeDirectory, ".rask", "tailwind");
}
