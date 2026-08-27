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
///     Resolves a native TypeScript tool — the esbuild bundler or the tsgo type checker — fetching it
///     once into a per-user cache if it is not already there.
/// </summary>
/// <remarks>
///     <para>
///         This is the Tailwind resolver's design, applied to a second pair of tools: pinned version,
///         per-user cache, checksum verified before anything is executed, and a single property that
///         turns the whole thing off. Read <c>Rask.Tailwind.Tasks.ResolveTailwindCliTask</c> alongside
///         it — the reasoning there for why a build-time download is acceptable at all (fetched once
///         rather than per build, pinned rather than floating, and switchable off) applies here
///         unchanged, and this repo has the scar that produced those three rules.
///     </para>
///     <para>
///         There is no npm fallback, and its absence is the point rather than an omission. Tailwind
///         needs one because it publishes no standalone binary for several platforms that its npm engine
///         does support. esbuild and tsgo publish native builds for every platform they support at all,
///         so a fallback through npm could only ever cover platforms where the direct download would
///         have worked — it would add a Node dependency and reach nothing new.
///     </para>
/// </remarks>
public sealed class ResolveTypeScriptToolTask : Task
{
    /// <summary><c>esbuild</c> or <c>tsgo</c>.</summary>
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
    [Required]
    public string CacheRoot { get; set; } = string.Empty;

    /// <summary>The registry to fetch from. Overridable for a mirror or an internal proxy.</summary>
    public string Registry { get; set; } = TypeScriptTools.DefaultRegistry;

    /// <summary>Refuse to fetch, and fail if nothing is cached.</summary>
    public bool Offline { get; set; }

    /// <summary>The executable to run.</summary>
    [Output]
    public string ToolPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!TryParseTool(Tool, out var tool))
        {
            Log.LogError($"Rask.TypeScript: '{Tool}' is not a tool this task knows; expected 'esbuild' or 'tsgo'.");
            return false;
        }

        var os = TypeScriptTools.CurrentOs();
        var packageName = os is null
            ? null
            : TypeScriptTools.PackageName(tool, os.Value, RuntimeInformation.ProcessArchitecture);

        if (os is null || packageName is null)
        {
            Log.LogError(
                $"Rask.TypeScript: {Tool} publishes no native build for this platform "
                + $"({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}), so Rask "
                + "cannot compile TypeScript here. Set RaskTypeScriptBuild=false to build without it.");
            return false;
        }

        var directory = TypeScriptTools.CacheDirectory(CacheRoot, tool, Version, packageName);
        var executable = Path.Combine(directory, TypeScriptTools.ExecutablePath(tool, os.Value));

        if (File.Exists(executable))
        {
            ToolPath = executable;
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
            Fetch(tool, packageName, directory, tarball);
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
                + $"'{TypeScriptTools.ExecutablePath(tool, os.Value)}'. This is a packaging change rather "
                + "than a problem with your project; please report it.");
            return false;
        }

        ToolPath = executable;
        return true;
    }

    private void Fetch(TypeScriptTool tool, string packageName, string directory, string tarballUrl)
    {
        Log.LogMessage(
            MessageImportance.High,
            $"Rask.TypeScript: fetching {packageName}@{Version} (one-off, cached in {CacheRoot})…");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        var metadata = http
            .GetStringAsync(TypeScriptTools.VersionDocumentUrl(Registry, packageName, Version))
            .GetAwaiter().GetResult();

        // Verified rather than trusted: these bytes are about to be executed by the build. A metadata
        // document that carries no SHA-512 is a failure, not "nothing to check".
        var expected = TypeScriptTools.ExpectedIntegrity(metadata);
        if (expected is null)
        {
            throw new IOException(
                $"the registry metadata for {packageName}@{Version} publishes no sha512 integrity, so the "
                + "download could not be verified");
        }

        var bytes = http.GetByteArrayAsync(tarballUrl).GetAwaiter().GetResult();
        var actual = Sha512Integrity(bytes);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new IOException(
                $"the download did not match its published checksum (expected {expected}, got {actual})");
        }

        // Unpacked beside the target and moved into place, so a cancelled build cannot leave a
        // half-extracted tree that every later build then treats as a cache hit. tsgo is ~27 MB across
        // ~115 files, and a truncated one of those is a compiler that reports nonsense about the DOM.
        var staging = directory + ".partial-" + Guid.NewGuid().ToString("n").Substring(0, 8);
        try
        {
            TarGz.ExtractTo(bytes, staging);
            MakeExecutable(Path.Combine(staging, TypeScriptTools.ExecutablePath(tool, TypeScriptTools.CurrentOs()!.Value)));

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
        using var chmod = Process.Start(new ProcessStartInfo("chmod")
        {
            Arguments = "+x \"" + path + "\"",
            UseShellExecute = false,
        });

        chmod?.WaitForExit();
    }

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
            default:
                tool = default;
                return false;
        }
    }
}
