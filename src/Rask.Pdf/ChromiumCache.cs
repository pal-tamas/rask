using System.Reflection;
using System.Runtime.InteropServices;

namespace Rask.Printing;

/// <summary>
/// Where the build put the pinned Chrome for Testing headless shell (build/Rask.Pdf.targets), and where the
/// container image puts it. Read only: the running app never downloads a browser.
/// </summary>
internal static class ChromiumCache
{
    /// <summary>Where the scaffolded Dockerfile copies the shell in the runtime image.</summary>
    internal const string ContainerPath = "/opt/rask/chrome/chrome-headless-shell";

    /// <summary>The pinned version this build of Rask.Pdf prints with.</summary>
    internal static string? Version { get; } = typeof(ChromiumCache).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(metadata => string.Equals(metadata.Key, "RaskPdfChromeVersion", StringComparison.Ordinal))?.Value;

    /// <summary>The pinned shell in the user's Rask cache, whether or not it is there yet.</summary>
    internal static string? PinnedShell() =>
        Version is { Length: > 0 } version && Platform() is { } platform && Root() is { } root
            ? Path.Combine(root, "chrome-headless-shell", version, platform, $"chrome-headless-shell-{platform}", Executable())
            : null;

    // The same folders build/Rask.Pdf.targets writes to.
    private static string? Root()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rask", "Cache");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Caches", "Rask");
        }

        return Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
            ? Path.Combine(xdg, "rask")
            : Path.Combine(home, ".cache", "rask");
    }

    private static string? Platform() => (OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), OperatingSystem.IsWindows(),
            RuntimeInformation.OSArchitecture) switch
    {
        (true, _, _, Architecture.X64) => "linux64",
        (true, _, _, Architecture.Arm64) => "linux-arm64",
        (_, true, _, Architecture.Arm64) => "mac-arm64",
        (_, true, _, Architecture.X64) => "mac-x64",
        (_, _, true, Architecture.X64) => "win64",
        _ => null,
    };

    private static string Executable() => OperatingSystem.IsWindows() ? "chrome-headless-shell.exe" : "chrome-headless-shell";
}
