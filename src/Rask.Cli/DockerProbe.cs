using System.ComponentModel;

namespace Rask.Cli;

/// <summary>
/// Detects the Docker CLI that <c>rask deploy</c> drives, and checks that a remote host's Docker daemon
/// is reachable over SSH. Unlike the EF tools (<see cref="EfToolProbe"/>), Docker is a heavyweight system
/// dependency we never auto-install — a missing docker prints install guidance and stops.
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

        console.Error.WriteLine("Docker isn't installed or isn't on your PATH. `rask deploy` uses the Docker CLI to build and run your app on the host.");
        console.Error.WriteLine("  Install Docker: https://docs.docker.com/get-docker/");
        return false;
    }

    /// <summary>
    /// True when the remote Docker daemon answers over SSH. <c>docker -H ssh://&lt;host&gt; version</c>
    /// exercises SSH auth and the remote daemon in one call, so a single check covers both failure modes.
    /// </summary>
    public static async Task<bool> CanReachHostAsync(IProcessRunner process, IConsole console, string host, CancellationToken cancellationToken)
    {
        var result = await process.CaptureAsync("docker", ["-H", $"ssh://{host}", "version"], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return true;
        }

        console.Error.WriteLine($"Couldn't reach the Docker daemon on '{host}' over SSH.");
        console.Error.WriteLine($"  • Make sure `ssh {host}` works non-interactively (key-based auth, host key already trusted).");
        console.Error.WriteLine("  • Make sure Docker is installed and running on the host, and your user can use it (member of the `docker` group).");
        return false;
    }
}
