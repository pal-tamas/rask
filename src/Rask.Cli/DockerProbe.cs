using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Rask.Cli;

/// <summary>
/// Detects the local Docker CLI that <c>rask deploy</c> drives.
///
/// <para><strong>We never auto-install Docker here</strong>, and unlike the EF tools
/// (<see cref="EfToolProbe"/>) that isn't inconsistency: on a developer machine Docker means Docker
/// Desktop or a system package manager, and silently installing either onto someone's laptop isn't
/// ours to do. A missing local docker prints the right command for the platform and stops.</para>
///
/// <para>The <em>remote</em> host is the opposite case, and <see cref="HostBootstrap"/> does install
/// Docker there: the box is precisely the thing the user is asking <c>rask deploy</c> to manage, and
/// making them SSH in to prepare it by hand is the seam this tool exists to close. Reachability is no
/// longer checked here either — <see cref="HostProbe"/> covers it in the same round-trip, and can tell
/// the four failure modes apart.</para>
/// </summary>
internal static class DockerProbe
{
    /// <summary>True when the local <c>docker</c> CLI is on the PATH (<c>docker --version</c> exits 0).</summary>
    public static async Task<bool> EnsureLocalAsync(IProcessRunner process, IConsole console, CancellationToken cancellationToken)
    {
        int exitCode;
        try
        {
            exitCode = (await process.CaptureAsync("docker", ["--version"], null, cancellationToken).ConfigureAwait(false)).ExitCode;
        }
        catch (Win32Exception)
        {
            // The docker binary isn't on the PATH at all — launching it throws rather than returning a
            // non-zero exit. Treat it the same as "not installed" and guide the user rather than crash.
            exitCode = -1;
        }

        if (exitCode == 0)
        {
            return true;
        }

        // Nothing is built locally — the build context ships to the host's daemon — but the CLI is
        // still the client for every `docker -H ssh://` call, so it has to be here.
        console.Error.WriteLine("Docker isn't installed or isn't on your PATH. `rask deploy` uses the Docker CLI to build and run your app on the host.");
        console.Error.WriteLine($"  Install Docker: {InstallHint()}");
        return false;
    }

    /// <summary>The install command for this machine — a command to paste beats a page to go read.</summary>
    private static string InstallHint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "brew install --cask docker (or https://docs.docker.com/get-docker/)";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "winget install Docker.DockerDesktop (or https://docs.docker.com/get-docker/)";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "curl -fsSL https://get.docker.com | sh (or https://docs.docker.com/get-docker/)";
        }

        return "https://docs.docker.com/get-docker/";
    }
}
