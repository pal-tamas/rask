using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     Resolves something that can run Tailwind: the standalone binary where one exists, npm where it
///     does not.
/// </summary>
/// <remarks>
///     <para>
///         Standalone first, because "the SDK is all you need" is why someone picks a C# host, and the
///         binary keeps that true on every platform Tailwind publishes one for — macOS, Linux (glibc and
///         musl) and Windows x64.
///     </para>
///     <para>
///         npm second, because it covers strictly more: Tailwind's npm engine ships native builds for
///         win32-arm64, 32-bit ARM and FreeBSD that the standalone release has no equivalent of, plus a
///         <c>wasm32-wasi</c> build that runs anywhere Node does. Falling back means no platform is
///         simply unsupported.
///     </para>
///     <para>
///         A build-time download is a real hazard and this repo has the scar: Litestream's build props
///         fetched a binary on every build, which broke offline builds and errored outright on a RID with
///         no published asset — hence <c>RaskLitestreamDownload=false</c>. Three things make this
///         different: it is fetched <b>once</b> into a per-user cache rather than per build, the version
///         is <b>pinned</b> rather than floating, and <c>RaskTailwindBuild=false</c> turns it off
///         entirely. <c>rask new --tailwind</c> also warms the cache while it is already online.
///     </para>
/// </remarks>
public sealed class ResolveTailwindCliTask : Task
{
    /// <summary>The pinned Tailwind version, without the leading <c>v</c>.</summary>
    [Required]
    public string Version { get; set; } = string.Empty;

    /// <summary>Where fetched binaries live. Shared by every project for this user.</summary>
    [Required]
    public string CacheRoot { get; set; } = string.Empty;

    /// <summary><c>auto</c>, <c>standalone</c> or <c>npm</c>.</summary>
    public string Engine { get; set; } = "auto";

    /// <summary>Refuse to fetch, and fail if nothing is cached.</summary>
    public bool Offline { get; set; }

    /// <summary>The executable to run.</summary>
    [Output]
    public string ToolPath { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the caller must go through npm: install Tailwind into the project, then run it from
    ///     the project's own <c>node_modules/.bin</c>.
    /// </summary>
    /// <remarks>
    ///     It cannot be an <c>npx</c> one-liner, and that is worth stating because it looks like it
    ///     should be. Tailwind resolves <c>@import "tailwindcss"</c> from the <b>stylesheet's own
    ///     directory</b> upward — not from the working directory, and not from whatever temporary prefix
    ///     npx assembles — so the package has to be reachable from the project itself. <c>--cwd</c> does
    ///     not change this. The standalone binary has no such requirement because it bundles Tailwind
    ///     inside itself, which is exactly why it is the preferred engine.
    /// </remarks>
    [Output]
    public bool UseNpm { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        var wanted = Engine?.Trim().ToLowerInvariant() ?? "auto";
        if (wanted == "npm")
        {
            return FallBackToNpm(becauseNoBinary: false);
        }

        var assetName = AssetForThisMachine();
        if (assetName is null)
        {
            if (wanted == "standalone")
            {
                Log.LogError(
                    "Rask.Tailwind: RaskTailwindEngine=standalone, but Tailwind publishes no standalone "
                    + $"binary for this platform ({RuntimeInformation.OSDescription}, "
                    + $"{RuntimeInformation.ProcessArchitecture}). Leave the engine on 'auto' to fall back "
                    + "to npm, or set RaskTailwindBuild=false.");
                return false;
            }

            // The whole reason the npm fallback exists.
            return FallBackToNpm(becauseNoBinary: true);
        }

        var path = TailwindCli.CachePath(CacheRoot, Version, assetName);
        if (File.Exists(path))
        {
            ToolPath = path;
            return true;
        }

        if (Offline)
        {
            Log.LogError(
                $"Rask.Tailwind: '{path}' is not there and RaskTailwindOffline is set, so it will not be "
                + $"fetched. Put {TailwindCli.DownloadUrl(Version, assetName)} at that path, or set "
                + "RaskTailwindBuild=false.");
            return false;
        }

        try
        {
            Fetch(assetName, path);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or WebException)
        {
            // Not fatal on its own: npm may still be able to run Tailwind, and a machine that cannot
            // reach GitHub releases can often still reach a registry mirror.
            Log.LogMessage(
                MessageImportance.High,
                $"Rask.Tailwind: could not fetch the standalone CLI ({ex.Message}); trying npm.");
            return FallBackToNpm(becauseNoBinary: false);
        }

        ToolPath = path;
        return true;
    }

    private bool FallBackToNpm(bool becauseNoBinary)
    {
        var npm = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "npm.cmd" : "npm";
        if (Which(npm) is null)
        {
            var why = becauseNoBinary
                ? "Tailwind publishes no standalone binary for this platform "
                  + $"({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}), so it "
                  + "needs Node.js"
                : "the npm engine was selected, so Tailwind needs Node.js";

            Log.LogError(
                $"Rask.Tailwind: {why}, and 'npm' is not on PATH. Install Node.js from https://nodejs.org "
                + "(macOS: brew install node; Windows: winget install OpenJS.NodeJS.LTS; Linux: your "
                + "distro's nodejs package), or set RaskTailwindBuild=false to build without the "
                + "stylesheet — the app still compiles and runs, it just has no generated CSS.");
            return false;
        }

        UseNpm = true;
        return true;
    }

    private void Fetch(string assetName, string path)
    {
        Log.LogMessage(
            MessageImportance.High,
            $"Rask.Tailwind: fetching the Tailwind {Version} CLI (one-off, cached in {CacheRoot})…");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var expected = TailwindCli.ExpectedChecksum(
            http.GetStringAsync(TailwindCli.ChecksumUrl(Version)).GetAwaiter().GetResult(),
            assetName);

        var bytes = http.GetByteArrayAsync(TailwindCli.DownloadUrl(Version, assetName))
            .GetAwaiter().GetResult();

        // Verified rather than trusted: this file is about to be executed by the build. A missing entry
        // in the manifest is a failure, not "nothing to check".
        if (expected is null)
        {
            throw new IOException(
                $"the release's sha256sums.txt does not list '{assetName}', so the download could not be verified");
        }

        var actual = Sha256(bytes);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"the download did not match its published checksum (expected {expected}, got {actual})");
        }

        // Written beside the target and moved into place, so a cancelled build cannot leave a truncated
        // binary that every later build then tries to execute.
        var partial = path + ".partial";
        File.WriteAllBytes(partial, bytes);
        MakeExecutable(partial);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(partial, path);
    }

    private static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Arguments rather than ArgumentList: this targets netstandard2.0, where the list form does not
        // exist. The path is ours and quoted, so a space in the cache directory is still safe.
        using var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chmod")
        {
            Arguments = "+x \"" + path + "\"",
            UseShellExecute = false,
        });

        chmod?.WaitForExit();
    }

    private static string? AssetForThisMachine()
    {
        TailwindOs os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = TailwindOs.MacOs;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = TailwindOs.Windows;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = TailwindOs.Linux;
        }
        else
        {
            // FreeBSD and anything else: no standalone build exists, and npm has one.
            return null;
        }

        return TailwindCli.AssetName(os, RuntimeInformation.ProcessArchitecture, IsMusl());
    }

    /// <summary>
    ///     Whether this is a musl libc distribution — Alpine and friends.
    /// </summary>
    /// <remarks>
    ///     Detected by the loader's own filename, the only thing reliably present. Getting it wrong hands
    ///     the build a glibc binary Alpine cannot load, and the error names neither Tailwind nor libc.
    /// </remarks>
    private static bool IsMusl()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            foreach (var directory in new[] { "/lib", "/usr/lib" })
            {
                if (Directory.Exists(directory) && Directory.GetFiles(directory, "ld-musl-*").Length > 0)
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            // Unreadable /lib is not worth failing over; glibc is the safe assumption.
        }

        return false;
    }

    private static string? Which(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(directory => directory.Length > 0)
            .Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(File.Exists);
}
