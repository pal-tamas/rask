using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Rask.Build;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     Resolves the Tailwind standalone binary: the one the project names, or the pinned release for this
///     machine, fetched once into a per-user cache.
/// </summary>
/// <remarks>
///     <para>
///         The standalone binary is the only engine, because "the SDK is all you need" is why someone
///         picks a C# host, and the binary keeps that true on every platform Tailwind publishes one for —
///         macOS, Linux (glibc and musl) and Windows (x64, and ARM64 under emulation).
///     </para>
///     <para>
///         Where Tailwind publishes none — 32-bit ARM, FreeBSD — the build fails and names
///         <c>RaskTailwindPath</c>: point it at a Tailwind binary for that machine and the build runs it as
///         it would the fetched one.
///     </para>
///     <para>
///         A build-time download is a real hazard and this repo has the scar: Litestream's build props
///         fetched a binary on every build, which broke offline builds and errored outright on a RID with
///         no published asset — hence <c>RaskLitestreamDownload=false</c> — and later failed every server
///         app's build during a GitHub outage, for a binary none of them needed. Three things make this
///         different: it is fetched <b>once</b> into a per-user cache rather than per build, the version
///         is <b>pinned</b> rather than floating, and <c>RaskTailwindPath</c> replaces the download
///         outright where one cannot work. The first build of a scaffolded app is what populates the cache;
///         <c>RaskTailwindCacheRoot</c> seeds it somewhere else, and <c>RaskTailwindOffline</c> makes an
///         unseeded cache an error instead of a download.
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

    /// <summary>
    ///     A Tailwind binary to run instead of the fetched one (<c>RaskTailwindPath</c>). Empty to fetch the
    ///     pinned release for this machine.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Refuse to fetch, and fail if nothing is cached.</summary>
    public bool Offline { get; set; }

    /// <summary>
    ///     The SHA-256 the downloaded binary must have, for a version Rask keeps no digest for
    ///     (<c>RaskTailwindSha256</c>). Empty for the pinned version, whose digests are in <see cref="TailwindPins" />.
    /// </summary>
    public string ExpectedSha256 { get; set; } = string.Empty;

    /// <summary>Where releases are downloaded from (<c>RaskTailwindReleaseBaseUrl</c>), for a mirror. Empty for GitHub.</summary>
    public string ReleaseBaseUrl { get; set; } = TailwindCli.DefaultReleaseBaseUrl;

    /// <summary>The pauses between attempts, in seconds, <c>;</c>-separated. Empty for the shared default.</summary>
    public string RetryWaits { get; set; } = string.Empty;

    /// <summary>The executable to run.</summary>
    [Output]
    public string ToolPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return UseNamedBinary(ExecutablePath.Trim());
        }

        var assetName = AssetForThisMachine();
        if (assetName is null)
        {
            Log.LogError(
                "Rask.Tailwind: Tailwind publishes no standalone binary for this platform "
                + $"({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}), so there "
                + "is nothing to fetch. Set RaskTailwindPath to a Tailwind v4 binary that runs here "
                + "(in the .csproj, or -p:RaskTailwindPath=/path/to/tailwindcss) and the build runs it. Rask "
                + "apps are styled with Tailwind, so there is no build without it: a project whose pages are "
                + "written in utilities and whose stylesheet was never compiled renders as unstyled HTML.");
            return false;
        }

        var path = TailwindCli.CachePath(CacheRoot, Version, assetName);
        if (TailwindCli.ReuseCached(path, m => Log.LogMessage(MessageImportance.High, m)))
        {
            ToolPath = path;
            return true;
        }

        if (Offline)
        {
            Log.LogError(
                $"Rask.Tailwind: '{path}' is not there and RaskTailwindOffline is set, so it will not be "
                + $"fetched. Download {TailwindCli.DownloadUrl(Version, assetName, ReleaseBaseUrl)} and put it at that "
                + "path — the cache is per user, so seeding it once serves every project on this "
                + "machine — set RaskTailwindPath to a Tailwind binary you already have, or unset "
                + "RaskTailwindOffline and let this build fetch it.");
            return false;
        }

        return FetchInto(assetName, path);
    }

    private bool UseNamedBinary(string executable)
    {
        if (!File.Exists(executable))
        {
            Log.LogError(
                $"Rask.Tailwind: RaskTailwindPath is '{executable}', and there is no file there. Point it at "
                + "a Tailwind v4 binary, or unset it to fetch the pinned release for this machine.");
            return false;
        }

        ToolPath = Path.GetFullPath(executable);
        return true;
    }

    private bool FetchInto(string assetName, string path)
    {
        try
        {
            Fetch(assetName, path);
        }
        catch (InvalidDataException ex)
        {
            // The bytes arrived and are not the ones expected, which is what a replaced release asset
            // looks like. Never quietly accepted.
            Log.LogError(
                $"Rask.Tailwind: the Tailwind {Version} CLI was downloaded but {ex.Message}. Nothing was "
                + "installed. If the release was republished, check it before setting RaskTailwindSha256 to "
                + "the new digest.");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or WebException
                                       or System.Threading.Tasks.TaskCanceledException)
        {
            var tried = TransientRetry.IsTransient(ex) ? $" (asked {TransientRetry.Parse(RetryWaits).Count + 1} times)" : "";
            Log.LogError(
                $"Rask.Tailwind: could not fetch the Tailwind {Version} CLI{tried} ({ex.Message}). Download "
                + $"{TailwindCli.DownloadUrl(Version, assetName, ReleaseBaseUrl)} and put it at '{path}' — the cache is per "
                + "user, so seeding it once serves every project on this machine — keep '"
                + CacheRoot + "' between CI runs so a release host's outage never reaches the build (actions/cache: "
                + "see https://rask.sh/docs/guides/deployment#caching-the-builds-tools-in-ci), or set "
                + "RaskTailwindPath to a Tailwind binary you already have.");
            return false;
        }

        ToolPath = path;
        return true;
    }

    private void Fetch(string assetName, string path)
    {
        Log.LogMessage(
            MessageImportance.High,
            $"Rask.Tailwind: fetching the Tailwind {Version} CLI (one-off, cached in {CacheRoot})…");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Install(DownloadVerified(assetName), path);
    }

    private byte[] DownloadVerified(string assetName)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var expected = RecordedChecksum(assetName) ?? PublishedChecksum(http, assetName);

        var bytes = Ask(() => HttpFetch.Bytes(http, TailwindCli.DownloadUrl(Version, assetName, ReleaseBaseUrl)));

        // Verified rather than trusted: this file is about to be executed by the build.
        var actual = Sha256(bytes);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            // Its own type, so the error says the bytes were wrong rather than that the network failed.
            throw new InvalidDataException($"its SHA-256 is {actual}, not the expected {expected}");
        }

        return bytes;
    }

    // The digest this build was told to expect, or the one recorded in the repository for the pinned
    // version. Null for a version nobody recorded one for.
    private string? RecordedChecksum(string assetName) =>
        string.IsNullOrWhiteSpace(ExpectedSha256) ? TailwindPins.For(Version, assetName) : ExpectedSha256.Trim();

    // The release's own manifest. It comes from where the binary does, so it proves the download arrived
    // intact and nothing about what was published — which the warning says.
    private string PublishedChecksum(HttpClient http, string assetName)
    {
        Log.LogWarning(
            $"Rask.Tailwind: Rask records no digest for Tailwind {Version} (it pins {TailwindPins.Version}), "
            + "so the CLI is checked only against the checksum its own release publishes. Set "
            + "RaskTailwindSha256 to the SHA-256 of the asset for this platform to pin it.");

        // A missing entry in the manifest is a failure, not "nothing to check".
        return TailwindCli.ExpectedChecksum(Ask(() => HttpFetch.Text(http, TailwindCli.ChecksumUrl(Version, ReleaseBaseUrl))), assetName)
               ?? throw new IOException(
                   $"the release's sha256sums.txt does not list '{assetName}', so the download could not be verified");
    }

    // A release host that fails for a moment (GitHub answering 502/504) is asked again; an answer that will not
    // change is not.
    private T Ask<T>(Func<T> request) =>
        TransientRetry.Run(
            request,
            TransientRetry.Parse(RetryWaits),
            (ex, next) => Log.LogMessage(
                MessageImportance.High,
                $"Rask.Tailwind: fetching the Tailwind {Version} CLI failed ({ex.Message}); asking again, attempt {next} of {TransientRetry.Parse(RetryWaits).Count + 1}…"));

    private static void Install(byte[] bytes, string path)
    {
        // Written beside the target and moved into place, so a cancelled build cannot leave a truncated
        // binary that every later build then tries to execute.
        //
        // UNIQUELY named, which is the whole fix for the failure this file's cache checks were chasing.
        // The partial used to be a single `path + ".partial"`, and the cache root is shared by every
        // project building at once — including the two target frameworks of ONE multi-targeted project,
        // which is why nightly failed inside Rask.Ui specifically. Two resolves racing on that one name
        // interleave their writes, and what lands is a file of the right name and the wrong bytes: MSB3073
        // over exit 126 or 127, from a download that reported success and verified its own checksum.
        var partial = path + "." + Guid.NewGuid().ToString("n") + ".partial";
        File.WriteAllBytes(partial, bytes);
        TailwindCli.MakeExecutable(partial);

        try
        {
            // Whoever gets there first wins, and the loser keeps the winner's copy rather than replacing
            // a file another build may be executing this instant. Both verified the same checksum.
            if (File.Exists(path))
            {
                File.Delete(partial);
                return;
            }

            File.Move(partial, path);

            // Only now, and only here: the receipt is what a later build checks the cache against, so it
            // must never sit beside anything whose checksum has not just been verified above.
            TailwindCli.WriteReceipt(path);
        }
        catch (IOException)
        {
            // Another build moved its own copy in between the check and the move. Theirs is as good.
            try
            {
                File.Delete(partial);
            }
            catch (IOException)
            {
                // A stray partial is harmless: nothing ever executes one.
            }
        }
    }

    private static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
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
            // FreeBSD and anything else: no standalone build exists; RaskTailwindPath is the way through.
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
}
