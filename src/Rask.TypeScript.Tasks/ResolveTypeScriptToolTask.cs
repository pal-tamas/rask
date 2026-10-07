using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.TypeScript.Tasks;

/// <summary>
///     Resolves a TypeScript tool — the esbuild bundler, the tsgo type checker, or the compiler library —
///     fetching it once into a per-user cache if it is not already there.
/// </summary>
/// <remarks>
///     <para>
///         This is the Tailwind resolver's design, applied to more tools: pinned version, per-user cache,
///         checksum verified before anything is executed, and a single property that turns the whole thing
///         off. Read <c>Rask.Tailwind.Tasks.ResolveTailwindCliTask</c> alongside it — the reasoning there for
///         why a build-time download is acceptable at all (fetched once rather than per build, pinned rather
///         than floating, and switchable off) applies here unchanged, and this repo has the scar that
///         produced those three rules.
///     </para>
///     <para>
///         There is no npm fallback, and its absence is the point rather than an omission. Tailwind
///         needs one because it publishes no standalone binary for several platforms that its npm engine
///         does support. esbuild and tsgo publish native builds for every platform they support at all,
///         so a fallback through npm could only ever cover platforms where the direct download would
///         have worked — it would add a Node dependency and reach nothing new. The compiler library is one
///         package for every platform, and pinning it here rather than taking the project's own copy is what
///         makes two machines extract the same props snapshot.
///     </para>
/// </remarks>
public sealed class ResolveTypeScriptToolTask : Task
{
    /// <summary><c>esbuild</c>, <c>tsgo</c> or <c>typescript</c>.</summary>
    [Required]
    public string Tool { get; set; } = string.Empty;

    /// <summary>The pinned version, without a leading <c>v</c>.</summary>
    /// <remarks>
    ///     Pinned rather than floating for the usual reproducibility reason, and for one specific to
    ///     tsgo: <c>@typescript/native-preview</c> publishes dated development builds to its
    ///     <c>latest</c> tag, so "latest" there means "whatever was built the morning you ran it".
    /// </remarks>
    [Required]
    public string Version { get; set; } = string.Empty;

    /// <summary>Where fetched tools live. Shared by every project for this user.</summary>
    /// <remarks>
    ///     No longer <c>[Required]</c>: left empty it falls back to the default root, so a caller with
    ///     no opinion gets the right place rather than a failure or, worse, a 27 MB unpack into a
    ///     directory named by the empty string.
    /// </remarks>
    public string CacheRoot { get; set; } = string.Empty;

    /// <summary>The registry to fetch from. Overridable for a mirror or an internal proxy.</summary>
    public string Registry { get; set; } = TypeScriptTools.DefaultRegistry;

    /// <summary>
    ///     The npm integrity (<c>sha512-…</c>) the downloaded package must have, for a version Rask keeps
    ///     no digest for. Empty for a pinned version, whose digests are in <see cref="TypeScriptToolPins" />.
    /// </summary>
    public string ExpectedIntegrity { get; set; } = string.Empty;

    /// <summary>Refuse to fetch, and fail if nothing is cached.</summary>
    public bool Offline { get; set; }

    /// <summary>Use the tool only if it is already cached: never fetch, and leave <see cref="ToolPath"/> empty rather than fail.</summary>
    /// <remarks>
    ///     For a design-time build. An IDE reload, or <c>dotnet format</c>, must never download a binary,
    ///     but once the tool is cached it may compile — and it has to, since user code calls members the
    ///     generator makes from tsgo's declarations.
    /// </remarks>
    public bool CachedOnly { get; set; }

    /// <summary>The executable to run — or, for the compiler library, the <c>typescript.js</c> to load.</summary>
    [Output]
    public string ToolPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!TryParseTool(Tool, out var tool))
        {
            Log.LogError(
                $"Rask.TypeScript: '{Tool}' is not a tool this task knows; expected 'esbuild', 'tsgo' or 'typescript'.");
            return false;
        }

        var os = TypeScriptTools.CurrentOs();
        var native = TypeScriptTools.IsNative(tool);

        // The compiler library is the same package on every platform, so only a native tool needs the OS
        // and architecture to be ones it publishes for. Any value stands in for an unknown OS here: the
        // library's name and layout do not depend on it.
        var platform = os ?? ToolOs.Linux;
        var packageName = os is null && native
            ? null
            : TypeScriptTools.PackageName(tool, platform, RuntimeInformation.ProcessArchitecture);

        if (packageName is null)
        {
            Log.LogError(
                $"Rask.TypeScript: {Tool} publishes no native build for this platform "
                + $"({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}), so Rask "
                + "cannot compile TypeScript here. Set RaskTypeScriptBuild=false to build without it.");
            return false;
        }

        // An omitted CacheRoot means the caller has no opinion, not that the cache belongs in a
        // directory named by the empty string — which is what Path.Combine would produce, putting a
        // 27 MB unpack wherever the process happened to be running.
        if (string.IsNullOrWhiteSpace(CacheRoot))
        {
            CacheRoot = TypeScriptTools.DefaultCacheRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return Resolve(tool, platform, packageName, native);
    }

    private bool Resolve(TypeScriptTool tool, ToolOs platform, string packageName, bool native)
    {
        var directory = TypeScriptTools.CacheDirectory(CacheRoot, tool, Version, packageName);
        var entry = TypeScriptTools.ExecutablePath(tool, platform);
        var executable = Path.Combine(directory, entry);

        if (File.Exists(executable))
        {
            ToolPath = executable;
            return true;
        }

        if (CachedOnly)
        {
            return true;
        }

        var tarball = TypeScriptTools.TarballUrl(Registry, packageName, Version);
        if (Offline)
        {
            Log.LogError(
                $"Rask.TypeScript: '{executable}' is not there and RaskTypeScriptOffline is set, so it will "
                + $"not be fetched. Unpack {tarball} into '{directory}' (dropping its leading package/ "
                + "directory), or set RaskTypeScriptBuild=false.");
            return false;
        }

        try
        {
            Fetch(packageName, directory, tarball, native ? entry : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or WebException or UnauthorizedAccessException)
        {
            // Fatal, unlike the Tailwind resolver's equivalent. There it can still try npm; here there is
            // nothing else to try, and continuing would produce an app whose TypeScript silently never
            // compiled.
            Log.LogError($"Rask.TypeScript: could not fetch {packageName}@{Version} — {ex.Message}");
            return false;
        }

        if (!File.Exists(executable))
        {
            Log.LogError(
                $"Rask.TypeScript: {packageName}@{Version} was fetched and unpacked, but it contains no "
                + $"'{entry}'. This is a packaging change rather than a problem with your project; please report it.");
            return false;
        }

        ToolPath = executable;
        return true;
    }

    /// <summary>Downloads, verifies and unpacks one package into the cache.</summary>
    /// <param name="packageName">The package to fetch.</param>
    /// <param name="directory">The cache directory it lands in.</param>
    /// <param name="tarballUrl">Where the tarball is.</param>
    /// <param name="executable">
    ///     The entry to mark executable, relative to the package root — null for the compiler library, a
    ///     JavaScript file Node loads rather than a binary anything runs.
    /// </param>
    private void Fetch(string packageName, string directory, string tarballUrl, string? executable)
    {
        Log.LogMessage(
            MessageImportance.High,
            $"Rask.TypeScript: fetching {packageName}@{Version} (one-off, cached in {CacheRoot})…");

        Unpack(DownloadVerified(packageName, tarballUrl), directory, executable);
    }

    private byte[] DownloadVerified(string packageName, string tarballUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // Verified rather than trusted: these bytes are about to be executed by the build.
        var expected = RecordedIntegrity(packageName) ?? PublishedIntegrity(http, packageName);

        var bytes = http.GetByteArrayAsync(tarballUrl).GetAwaiter().GetResult();
        var actual = Sha512Integrity(bytes);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new IOException(
                $"the download is not the package that was expected (integrity {actual}, not {expected}). "
                + "Nothing was installed; if the package was republished, check it before pinning the new digest");
        }

        return bytes;
    }

    // The integrity this build was told to expect, or the one recorded in the repository for a pinned
    // version. Null for a version nobody recorded one for.
    private string? RecordedIntegrity(string packageName) =>
        string.IsNullOrWhiteSpace(ExpectedIntegrity)
            ? TypeScriptToolPins.For(packageName, Version)
            : ExpectedIntegrity.Trim();

    // The registry's own metadata. It comes from where the tarball does — and the registry is a setting —
    // so it proves the download arrived intact and nothing about what was published, which the warning says.
    private string PublishedIntegrity(HttpClient http, string packageName)
    {
        Log.LogWarning(
            $"Rask.TypeScript: Rask records no digest for {packageName}@{Version}, so it is checked only "
            + $"against the integrity its own registry publishes. {HowToPin()}");

        var metadata = http
            .GetStringAsync(TypeScriptTools.VersionDocumentUrl(Registry, packageName, Version))
            .GetAwaiter().GetResult();

        // A metadata document that carries no SHA-512 is a failure, not "nothing to check".
        return TypeScriptTools.ExpectedIntegrity(metadata)
               ?? throw new IOException(
                   $"the registry metadata for {packageName}@{Version} publishes no sha512 integrity, so the "
                   + "download could not be verified");
    }

    // The MSBuild property that carries ExpectedIntegrity for this tool, named so the warning can be acted on.
    // What to do about an unpinned version, so the warning can be acted on. An app sets a property for the
    // two tools its own build resolves; esbuild is only ever fetched by Rask's own build, where the answer
    // is to record the digest.
    private string HowToPin()
    {
        const string Record = "Record its dist.integrity in TypeScriptToolPins.cs.";
        if (!TryParseTool(Tool, out var tool))
        {
            return Record;
        }

        return tool switch
        {
            TypeScriptTool.Tsgo => "Set RaskTsgoIntegrity to the package's sha512 integrity to pin it.",
            TypeScriptTool.TypeScript => "Set RaskExternalTypeScriptIntegrity to the package's sha512 integrity to pin it.",
            _ => Record,
        };
    }

    private static void Unpack(byte[] bytes, string directory, string? executable)
    {
        // Unpacked beside the target and moved into place, so a cancelled build cannot leave a
        // half-extracted tree that every later build then treats as a cache hit. tsgo is ~27 MB across
        // ~115 files, and a truncated one of those is a compiler that reports nonsense about the DOM.
        var staging = directory + ".partial-" + Guid.NewGuid().ToString("n").Substring(0, 8);
        try
        {
            TarGz.ExtractTo(bytes, staging);
            if (executable is not null)
            {
                MakeExecutable(Path.Combine(staging, executable));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            if (Directory.Exists(directory))
            {
                // Another build won the race while this one was downloading. Its copy passed the same
                // checksum, so it is the same tree; keeping it avoids deleting a file that process may
                // be executing right now.
                return;
            }

            Directory.Move(staging, directory);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (IOException)
                {
                    // A leftover staging directory is litter in a cache, not a build failure.
                }
            }
        }
    }

    /// <summary>The Subresource-Integrity form npm publishes: <c>sha512-</c> then base64, not hex.</summary>
    private static string Sha512Integrity(byte[] bytes)
    {
        using var sha = SHA512.Create();
        return "sha512-" + Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !File.Exists(path))
        {
            return;
        }

        // The tar entry carries mode 0755, but nothing above applies it — writing through File.Create
        // takes the process umask instead. Without this the download succeeds and the first invocation
        // fails with "permission denied", naming a path rather than a cause.
        //
        // Arguments rather than ArgumentList: this targets netstandard2.0, where the list form does not
        // exist. The path is ours and quoted, so a space in the cache directory is still safe.
        using var chmod = Process.Start(new ProcessStartInfo(ChmodPath)
        {
            Arguments = "+x \"" + path + "\"",
            UseShellExecute = false,
        });

        chmod?.WaitForExit();
    }

    // An absolute path, so a PATH entry cannot stand in for chmod. /bin is the norm; NixOS has neither.
    private static readonly string ChmodPath =
        Array.Find(
            new[] { "/bin/chmod", "/usr/bin/chmod", "/run/current-system/sw/bin/chmod" },
            File.Exists)
        ?? "/bin/chmod";

    private static bool TryParseTool(string value, out TypeScriptTool tool)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "esbuild":
                tool = TypeScriptTool.Esbuild;
                return true;
            case "tsgo":
                tool = TypeScriptTool.Tsgo;
                return true;
            case "typescript":
                tool = TypeScriptTool.TypeScript;
                return true;
            default:
                tool = default;
                return false;
        }
    }
}
