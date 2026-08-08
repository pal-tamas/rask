using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Rask.Cli.Tests;

/// <summary>
/// Shared plumbing for the deploy gate: a throwaway <b>container acting as a bare VPS</b> that the real
/// <c>rask deploy</c> is pointed at, over real SSH, driving a real Docker daemon.
///
/// <para>Every other deploy test in this assembly is mocked — <see cref="FakeProcessRunner"/> records the
/// argv and hands back a scripted exit code, so they prove the command line Rask <em>builds</em> and
/// nothing about whether it <em>works</em>. That left the whole deploy path (a Dockerfile that has to
/// build, a container that has to answer, a Caddyfile a real Caddy has to accept, a volume that has to
/// survive) unverified. These tests close that gap without a cloud account: the "host" is a privileged
/// <c>docker:dind</c> container running its own dockerd and an sshd.</para>
/// </summary>
internal static class DeployE2E
{
    /// <summary>The dind image the fake VPS is built on. Pinned: 27-dind's alpine index no longer resolves openssh-server.</summary>
    internal const string DindImage = "docker.io/library/docker:28-dind";

    internal const string SkipReason =
        "Deploy host gate: set RASK_DEPLOY_E2E=1 to run it. It needs a local `docker` CLI and a daemon that " +
        "can run a privileged container (Docker Desktop, Colima, or podman). See scripts/run-deploy-e2e-local.sh.";

    /// <summary>True when the deploy-host gate is opted into (it builds images and boots a privileged container).</summary>
    internal static bool Enabled => Environment.GetEnvironmentVariable("RASK_DEPLOY_E2E") == "1";

    /// <summary>Run a process to completion, capturing both streams. Used for the harness's own docker/ssh calls.</summary>
    internal static async Task<(int Exit, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }

    /// <summary>A TCP port nothing is listening on, for the fake VPS's published sshd.</summary>
    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// The real <see cref="ProcessRunner"/> behaviour with extra environment variables layered on — the seam
/// that lets the gate run the genuine <c>rask deploy</c> code path while keeping its SSH identity, config,
/// and known-hosts entirely inside the test's temp directory.
///
/// <para>Why this instead of pointing <c>HOME</c> at a temp dir: on macOS OpenSSH resolves <c>~/.ssh</c>
/// through <c>getpwuid()</c>, <b>not</b> <c>$HOME</c>, so a redirected HOME is silently ignored and ssh
/// reads the developer's real config. The fixture therefore puts an <c>ssh</c> shim first on <c>PATH</c>
/// (it re-execs the real ssh with <c>-F &lt;temp config&gt;</c>), which both Rask's own
/// <c>ssh</c> calls and Docker's <c>ssh://</c> transport pick up — neither ever learns it isn't the real
/// binary, and the developer's <c>~/.ssh</c> is never read or written.</para>
/// </summary>
internal sealed class EnvScopedProcessRunner(
    IReadOnlyDictionary<string, string> environment,
    IReadOnlyDictionary<string, string> executables) : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        // This shim already applies its own fixed environment; the overlay parameter is unused here.
        var (exit, _) = await CaptureCoreAsync(fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        return exit;
    }

    public async Task<int> RunTeeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        // Deploy never tees; the shim captures, then replays the lines so the contract still holds.
        var (exit, output) = await CaptureCoreAsync(fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        foreach (var line in output.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onLine(line.TrimEnd('\r'));
        }

        return exit;
    }

    public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var (exit, output) = await CaptureCoreAsync(fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        return new ProcessResult(exit, output.StandardOutput, output.StandardError);
    }

    private async Task<(int Exit, (string StandardOutput, string StandardError) Streams)> CaptureCoreAsync(
        string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        // A bare "ssh" would be resolved against the PARENT process's PATH — .NET does the executable
        // lookup itself, before the child's environment applies — so prefixing PATH is not enough to
        // redirect Rask's own ssh calls. Substituting the absolute path of the shim is. (Docker's
        // ssh:// transport looks ssh up in its own environment, which the PATH entry below does cover.)
        var info = new ProcessStartInfo(executables.TryGetValue(fileName, out var resolved) ? resolved : fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, (await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false)));
    }
}
