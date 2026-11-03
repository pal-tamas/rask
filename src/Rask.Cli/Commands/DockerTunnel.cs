using System.ComponentModel;

namespace Rask.Cli.Commands;

/// <summary>
/// One SSH connection to the box for a whole deploy: the box's docker socket forwarded to a socket of this machine's
/// (<c>ssh -N -L &lt;local&gt;:/var/run/docker.sock</c>), which every docker call of the deploy then talks to.
///
/// <para>Without it each call is <c>docker -H ssh://</c>, and each of those is a new ssh login — the handshake, the
/// box's PAM session (on Ubuntu its motd scripts), and a <c>docker system dial-stdio</c> process started on the box —
/// before the call does anything. A deploy makes some forty calls, and the build several more dials of its own, so on a
/// box where a login costs five seconds that was minutes of nothing. Over the forward a call is a channel on a
/// connection already open.</para>
///
/// <para>The local socket sits in a directory only this user can enter, since whoever reaches it reaches the box's
/// docker daemon. When the forward can't be had — a box that refuses socket forwarding, a daemon somewhere else than
/// <see cref="RemoteSocket"/>, Windows — the deploy goes on with a connection per call, as before. Every check, lock,
/// gate and rollback runs the same either way: only the transport differs.</para>
/// </summary>
internal sealed class DockerTunnel : IAsyncDisposable
{
    /// <summary>Where the box's docker daemon listens — what <c>docker -H ssh://</c> reaches on a stock install.</summary>
    internal const string RemoteSocket = "/var/run/docker.sock";

    private readonly CancellationTokenSource _stop;
    private readonly Task<int> _ssh;
    private readonly string _directory;
    private string? _probeError;
    private bool _refused;

    private DockerTunnel(string host, string directory, CancellationTokenSource stop, Task<int> ssh)
    {
        Replaces = $"ssh://{host}";
        Endpoint = $"unix://{SocketIn(directory)}";
        _directory = directory;
        _stop = stop;
        _ssh = ssh;
    }

    /// <summary>The <c>-H</c> value the tunnel stands in for: <c>ssh://&lt;host&gt;</c>.</summary>
    public string Replaces { get; }

    /// <summary>The <c>-H</c> value that reaches the box over the tunnel: <c>unix://&lt;local socket&gt;</c>.</summary>
    public string Endpoint { get; }

    /// <summary>Whether the connection is still up. A tunnel that dropped is not used again.</summary>
    public bool IsOpen => !_ssh.IsCompleted;

    /// <summary>
    /// The ssh that holds the forward: no command, no tty, no stdin; gives up if the forward can't be made. Its log goes
    /// to <paramref name="logFile"/> (<c>-E</c>), not the deploy's terminal: a refused connection is read back from there
    /// and said once, as the reason the deploy goes on without the tunnel.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(SshTarget target, string localSocket, string logFile) =>
    [
        "-n", "-N", "-T",
        "-o", "ExitOnForwardFailure=yes",
        "-o", "StreamLocalBindUnlink=yes",
        "-o", "ServerAliveInterval=15",
        "-o", "ServerAliveCountMax=4",
        // INFO, ssh's default: a refused forward is logged at INFO, and -E keeps it all off the deploy's terminal.
        "-o", "LogLevel=INFO",
        "-E", logFile,
        "-L", $"{localSocket}:{RemoteSocket}",
        .. target.ConnectionArguments(freshConnection: true),
    ];

    /// <summary>The docker call that tells whether the forward reaches the daemon yet.</summary>
    internal static IReadOnlyList<string> BuildProbeArguments(string endpoint) =>
        ["-H", endpoint, "version", "--format", "{{.Server.Version}}"];

    internal static string SocketIn(string directory) => Path.Combine(directory, "docker.sock");

    internal static string LogIn(string directory) => Path.Combine(directory, "ssh.log");

    /// <summary>
    /// Open the forward to <paramref name="host"/> and wait until docker answers through it. The tunnel, or why there is
    /// none — the caller then goes on without one.
    /// </summary>
    internal static async Task<(DockerTunnel? Tunnel, string? Failure)> OpenAsync(
        IProcessRunner process, string host, ReadinessPoll wait, Func<string> createPrivateDirectory, CancellationToken cancellationToken)
    {
        if (!SshTarget.TryParse(host, out var target, out var invalid))
        {
            return (null, invalid);
        }

        var directory = createPrivateDirectory();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<int> ssh;
        try
        {
            ssh = process.RunAsync("ssh", BuildArguments(target, SocketIn(directory), LogIn(directory)), null, stop.Token);
        }
        catch (Win32Exception e)
        {
            stop.Dispose();
            TryDeleteDirectory(directory);
            return (null, e.Message);
        }

        var tunnel = new DockerTunnel(host, directory, stop, ssh);
        bool reached;
        try
        {
            reached = await wait.UntilAsync(
                async () => !tunnel.IsOpen || tunnel.WasRefused() || await tunnel.AnswersAsync(process, wait.Timeout, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Stopped while waiting: the ssh must not outlive the deploy that started it.
            await tunnel.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (reached && tunnel.IsOpen && !tunnel._refused)
        {
            return (tunnel, null);
        }

        // Read before the dispose: the log lives in the directory the dispose removes.
        var failure = tunnel.Failure(wait.Timeout);
        await tunnel.DisposeAsync().ConfigureAwait(false);
        return (null, failure);
    }

    // A refused connection is final — sshd won't change its mind in twenty seconds — so the wait ends on it.
    private bool WasRefused()
    {
        _refused = SshForwardLog.IsRefusal(SshForwardLog.LastLine(LogIn(_directory)));
        return _refused;
    }

    // What went wrong, and in whose words: ssh's own last line when it said something, else what docker said last.
    private string Failure(TimeSpan timeout)
    {
        var said = SshForwardLog.LastLine(LogIn(_directory));
        if (SshForwardLog.IsRefusal(said))
        {
            return SshForwardLog.Explain(said);
        }

        var what = IsOpen ? $"docker did not answer through it within {StopTimeout.Format(timeout)}" : "ssh would not hold the forward";
        var why = said ?? _probeError;
        return why is null ? what : $"{what}: {why}";
    }

    // One ask, held to the whole wait: a forward that accepted the connection but carries nothing must not hang the deploy.
    private async Task<bool> AnswersAsync(IProcessRunner process, TimeSpan limit, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(limit);
        try
        {
            var probe = await process.CaptureAsync("docker", BuildProbeArguments(Endpoint), null, bounded.Token).ConfigureAwait(false);
            _probeError = probe.ExitCode == 0 ? null : SshForwardLog.LastNonEmpty(probe.StandardError);
            return probe.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _probeError = $"one ask took longer than {StopTimeout.Format(limit)}";
            return false;
        }
    }

    /// <summary>The default private directory: created by .NET owner-only (0700) on Unix.</summary>
    internal static string CreateTempDirectory() => Directory.CreateTempSubdirectory("rask-").FullName;

    /// <summary>Close the connection and remove the local socket. Never throws: the deploy's own result is what counts.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _ssh.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped, as asked: the runner killed the ssh.
        }
        catch (Win32Exception)
        {
            // It never started; nothing to stop.
        }

        _stop.Dispose();
        TryDeleteDirectory(_directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Already gone, or the socket still held for a moment; the temp dir is the OS's to sweep.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
