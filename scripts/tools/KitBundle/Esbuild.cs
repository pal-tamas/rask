using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Rask.TypeScript.Tasks;

namespace KitBundle;

/// <summary>The native esbuild the Rask build bundles with: its version, its binary, and a run of it.</summary>
/// <remarks>
///     The same binary the build's ResolveTypeScriptToolTask fetches: the version Rask.Core.targets pins, the
///     platform package and per-user cache directory <see cref="TypeScriptTools" /> names, and the SHA-512
///     <see cref="TypeScriptToolPins" /> records — refused when none is recorded, since it is about to run here.
///     <c>RASK_KITBUNDLE_ESBUILD</c> names another binary (a test's stand-in).
/// </remarks>
internal static class Esbuild
{
    /// <summary>The version every bundle is built with: Rask.Core.targets' <c>RaskEsbuildVersion</c>.</summary>
    internal static string Version { get; } = PinnedVersion();

    /// <summary>Runs esbuild in <paramref name="workingDirectory" />, failing on anything but a clean exit.</summary>
    internal static async Task Run(string workingDirectory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(await Ensure()) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new KitBundleException($"esbuild {Version} did not start");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new KitBundleException($"esbuild {Version} failed (exit {process.ExitCode})");
        }
    }

    /// <summary>The binary, fetched into the per-user cache the first time.</summary>
    internal static async Task<string> Ensure()
    {
        if (Environment.GetEnvironmentVariable("RASK_KITBUNDLE_ESBUILD") is { Length: > 0 } stand)
        {
            return stand;
        }

        var os = TypeScriptTools.CurrentOs() ?? throw new KitBundleException("esbuild publishes no build for this system");
        var package = TypeScriptTools.PackageName(TypeScriptTool.Esbuild, os, RuntimeInformation.ProcessArchitecture)
                      ?? throw new KitBundleException($"esbuild publishes no build for {RuntimeInformation.ProcessArchitecture}");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = TypeScriptTools.CacheDirectory(TypeScriptTools.DefaultCacheRoot(home), TypeScriptTool.Esbuild, Version, package);
        var executable = Path.Combine(directory, TypeScriptTools.ExecutablePath(TypeScriptTool.Esbuild, os));
        if (File.Exists(executable))
        {
            return executable;
        }

        var integrity = TypeScriptToolPins.For(package, Version)
                        ?? throw new KitBundleException($"TypeScriptToolPins.cs records no SHA-512 for {package}@{Version}");
        var tarball = await NpmRegistry.Tarball(TypeScriptTools.TarballUrl(TypeScriptTools.DefaultRegistry, package, Version), integrity);

        // Unpacked beside the cache directory and moved into place, as the build does, so a run cut short leaves no
        // half-written binary that every later build would take for a cached one.
        var staging = directory + ".partial-" + Guid.NewGuid().ToString("n")[..8];
        foreach (var (path, bytes) in Installer.Files(tarball))
        {
            var target = Path.Combine(staging, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(staging, TypeScriptTools.ExecutablePath(TypeScriptTool.Esbuild, os)),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        if (Directory.Exists(directory))
        {
            Directory.Delete(staging, recursive: true);
        }
        else
        {
            Directory.Move(staging, directory);
        }

        return executable;
    }

    private static string PinnedVersion()
    {
        var targets = Path.Combine(Repository.Root, "src", "Rask.Core", "build", "Rask.Core.targets");
        return XDocument.Load(targets).Descendants().FirstOrDefault(e => e.Name.LocalName == "RaskEsbuildVersion")?.Value.Trim()
               ?? throw new KitBundleException($"{targets} pins no RaskEsbuildVersion");
    }
}
