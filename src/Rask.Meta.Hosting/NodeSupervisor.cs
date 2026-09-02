using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Meta.Hosting;

/// <summary>
///     Owns the meta framework's Node process for the lifetime of the app: starts it, waits for it to
///     listen, streams its output into <see cref="ILogger" />, restarts it when it dies, and stops the
///     whole host when it will not stay up.
/// </summary>
/// <remarks>
///     <para>
///         The process is a child of this one, so there is exactly one runtime owning lifetime and one
///         place logs come out. The cost of that choice, taken deliberately, is that restart and
///         backoff policy now live here rather than in an init system.
///     </para>
///     <para>
///         It is bound to <c>127.0.0.1</c> and never <c>0.0.0.0</c>. Kestrel is the only thing that
///         should be able to reach it, so publishing the container's ports must not expose an
///         unauthenticated server-side renderer alongside the app.
///     </para>
/// </remarks>
internal sealed partial class NodeSupervisor : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<NodeSupervisor> _logger;
    private readonly MetaHostingOptions _options;
    private readonly MetaPaths _paths;
    private readonly NodeReadiness _readiness;

    // Public on an internal type, for the same reason as NodeForwarder: the container resolves only
    // public constructors, and an internal type's members are not public API.
    public NodeSupervisor(
        MetaHostingOptions options,
        MetaPaths paths,
        NodeReadiness readiness,
        IHostApplicationLifetime lifetime,
        ILogger<NodeSupervisor> logger)
    {
        _options = options;
        _paths = paths;
        _readiness = readiness;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>The absolute path of the server entry this supervisor runs.</summary>
    internal string ServerEntryPath => _paths.ServerEntry;

    /// <summary>
    ///     Refuses to start at all when there is no built front end to run.
    /// </summary>
    /// <remarks>
    ///     Checked here, in <see cref="IHostedService.StartAsync" />, rather than in the loop below,
    ///     because the two produce very different failures. Throwing here aborts startup with the
    ///     message naming the path that is missing. Calling <c>StopApplication()</c> from the loop
    ///     instead cancels Kestrel's own <c>BindAsync</c> mid-startup, and the process then dies with
    ///     <c>TaskCanceledException</c> — which says nothing about the front end and buries the
    ///     Critical line that did. "You have not built the front end" is a configuration mistake, and a
    ///     configuration mistake should fail fast and name itself.
    /// </remarks>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.SuperviseNode && !File.Exists(ServerEntryPath))
        {
            throw new InvalidOperationException(
                $"Rask.Meta.Hosting: no {_options.Framework.Name} server entry at '{ServerEntryPath}'. "
                + "Build the front end, or set MetaHostingOptions.AppDirectory to where it was built.");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    /// <summary>
    ///     The supervision loop, separated from <see cref="ExecuteAsync" /> so it can be driven
    ///     directly. What is worth testing here is the policy — when it gives up, when it refuses to
    ///     start — and reaching that through <see cref="BackgroundService" />'s start semantics tests
    ///     the host rather than this.
    /// </summary>
    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.SuperviseNode)
        {
            // Someone else is running the front end. Nothing to supervise, and nothing to wait for.
            _readiness.MarkReady();
            return;
        }

        var entry = ServerEntryPath;
        if (!File.Exists(entry))
        {
            LogEntryMissing(_options.Framework.Name, entry);
            _lifetime.StopApplication();
            return;
        }

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (attempt > 0)
            {
                if (attempt > _options.MaxRestartAttempts)
                {
                    break;
                }

                var backoff = BackoffFor(attempt);
                LogRestarting(_options.Framework.Name, attempt, _options.MaxRestartAttempts, backoff.TotalSeconds);
                try
                {
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            var startedAt = TimeProvider.System.GetTimestamp();
            await RunOnceAsync(entry, stoppingToken).ConfigureAwait(false);
            var lasted = TimeProvider.System.GetElapsedTime(startedAt);

            attempt = NextAttempt(attempt, lasted, _options.HealthyRunThreshold);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // The budget is spent. Stopping the host is the honest outcome: an orchestrator restarting the
        // container is a better supervisor than this loop, and an exit is visible where a permanently
        // degraded process that still answers health checks is not.
        LogGivingUp(_options.Framework.Name, _options.MaxRestartAttempts);
        _lifetime.StopApplication();
    }

    /// <summary>
    ///     The restart budget after a run that lasted <paramref name="lasted" />.
    /// </summary>
    /// <remarks>
    ///     A run that stayed up counts as recovery rather than as another strike. Without this the
    ///     budget is a <em>lifetime</em> one: a server that runs happily for a week and crashes once a
    ///     month still takes the host down on its fifth crash, months apart — which is not what "will
    ///     not stay running" means. Consecutive failures are the signal; scattered ones are not.
    /// </remarks>
    internal static int NextAttempt(int attempt, TimeSpan lasted, TimeSpan healthyThreshold) =>
        lasted >= healthyThreshold ? 1 : attempt + 1;

    /// <summary>
    ///     Exponential backoff, capped — 1s, 2s, 4s, 8s, 16s, then 30s for anything beyond.
    /// </summary>
    internal static TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(30d, Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Runs the process once, returning when it exits or the host is stopping.</summary>
    private async Task RunOnceAsync(string entry, CancellationToken stoppingToken)
    {
        using var process = new Process { StartInfo = BuildStartInfo(entry), EnableRaisingEvents = true };

        // stdout and stderr are BOTH Information. A great deal of Node tooling writes ordinary
        // progress to stderr, so mapping it to Error would fill the log with warnings that mean
        // nothing. What actually signals a fault is the exit code, which is reported below.
        process.OutputDataReceived += (_, e) => LogNodeOutput(e.Data);
        process.ErrorDataReceived += (_, e) => LogNodeOutput(e.Data);

        if (!process.Start())
        {
            LogStartFailed(_options.Framework.Name);
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        LogStarted(_options.Framework.Name, process.Id, _options.Port);

        try
        {
            var ready = await WaitForListeningAsync(process, stoppingToken).ConfigureAwait(false);
            if (ready)
            {
                _readiness.MarkReady();
                LogReady(_options.Framework.Name, _options.Port);
            }
            else if (!process.HasExited && !stoppingToken.IsCancellationRequested)
            {
                // The budget exists to end exactly this state. A process that is alive but has not
                // bound its port never exits on its own, so waiting for it below would hold the app at
                // 503 for ever without ever spending a restart attempt — the "degraded process that
                // still answers" outcome this class refuses. Abandoning the attempt hands it to the
                // backoff loop, which eventually gives up and stops the host.
                return;
            }

            await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
            _readiness.MarkNotReady();

            if (!stoppingToken.IsCancellationRequested)
            {
                LogExited(_options.Framework.Name, process.ExitCode);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down, not a crash.
        }
        finally
        {
            _readiness.MarkNotReady();
            await StopProcessAsync(process).ConfigureAwait(false);
        }
    }

    private ProcessStartInfo BuildStartInfo(string entry)
    {
        var info = new ProcessStartInfo(_options.NodeExecutable)
        {
            WorkingDirectory = _paths.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.ArgumentList.Add(entry);

        info.Environment[_options.Framework.PortVariable] =
            _options.Port.ToString(CultureInfo.InvariantCulture);
        info.Environment[_options.Framework.HostVariable] = "127.0.0.1";

        // Set rather than overridden: Next in particular logs a warning on every request without it,
        // but an app that has deliberately chosen otherwise keeps its choice.
        if (!info.Environment.ContainsKey("NODE_ENV"))
        {
            info.Environment["NODE_ENV"] = "production";
        }

        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            info.Environment[_options.BaseUrlVariable] = _options.BaseUrl;
        }

        foreach (var (key, value) in _options.Environment)
        {
            info.Environment[key] = value;
        }

        return info;
    }

    /// <summary>
    ///     Polls a loopback connect until the process accepts one, it exits, or the startup budget runs
    ///     out.
    /// </summary>
    /// <remarks>
    ///     A TCP connect rather than a sentinel line on stdout: every one of the six frameworks
    ///     announces itself differently, and some say nothing at all, but all of them are only useful
    ///     once the port answers. Watching the port is the one signal that means the same thing
    ///     everywhere.
    /// </remarks>
    private async Task<bool> WaitForListeningAsync(Process process, CancellationToken stoppingToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;

        while (!stoppingToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync("127.0.0.1", _options.Port, stoppingToken).ConfigureAwait(false);

                // A connect proves something owns the port, not that OUR child does. If the child
                // failed to bind because another process already had it, the probe would happily
                // succeed against that stranger and we would forward this app's traffic to it.
                return !process.HasExited;
            }
            catch (SocketException)
            {
                // Not listening yet.
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            LogStartupTimedOut(_options.Framework.Name, _options.StartupTimeout.TotalSeconds);
        }

        return false;
    }

    /// <summary>
    ///     Asks the process to stop, then insists.
    /// </summary>
    /// <remarks>
    ///     <see cref="Process.Kill()" /> is <c>SIGKILL</c> on Unix, which drops in-flight renders on
    ///     the floor. A well-behaved shutdown sends <c>SIGTERM</c> first and gives the framework its
    ///     own grace period — so the signal goes through libc, and <see cref="Process.Kill(bool)" />
    ///     stays the fallback for a process that ignores it and for Windows, which has no equivalent.
    /// </remarks>
    private async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            if (!OperatingSystem.IsWindows() && Terminate(process.Id, SIGTERM) == 0)
            {
                using var grace = new CancellationTokenSource(_options.ShutdownTimeout);
                try
                {
                    await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    LogGraceExpired(_options.Framework.Name, _options.ShutdownTimeout.TotalSeconds);
                }
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the signal.
        }
        catch (NotSupportedException)
        {
            // No process tree to walk on this platform.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The kill itself was refused — a partially exited tree, or no permission to signal one of
            // its members. Caught because this runs inside RunOnceAsync's finally: letting it escape
            // would leave the supervision loop through BackgroundServiceExceptionBehavior.StopHost and
            // take the whole app down, when the process we were trying to end is already going away.
        }
    }

    private const int SIGTERM = 15;

    /// <remarks>
    ///     <c>DllImport</c> rather than the newer <c>LibraryImport</c>: the source generator behind
    ///     that attribute emits unsafe code, so it would mean turning on
    ///     <c>AllowUnsafeBlocks</c> for the whole package to gain nothing here. Both arguments are
    ///     blittable <see cref="int" />s, which is the case where the two are equivalent and there is
    ///     no marshalling for a generator to improve on.
    /// </remarks>
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Terminate(int pid, int signal);

    private void LogNodeOutput(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            LogNodeLine(_options.Framework.Name, line);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Framework}: {Line}")]
    private partial void LogNodeLine(string framework, string line);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "{Framework} starting (pid {ProcessId}) on 127.0.0.1:{Port}.")]
    private partial void LogStarted(string framework, int processId, int port);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "{Framework} is listening on 127.0.0.1:{Port}; forwarding enabled.")]
    private partial void LogReady(string framework, int port);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "{Framework} exited with code {ExitCode}.")]
    private partial void LogExited(string framework, int exitCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Restarting {Framework} (attempt {Attempt} of {MaxAttempts}) in {Seconds}s.")]
    private partial void LogRestarting(string framework, int attempt, int maxAttempts, double seconds);

    [LoggerMessage(EventId = 6, Level = LogLevel.Critical,
        Message = "{Framework} failed to stay running after {MaxAttempts} restarts; stopping the application.")]
    private partial void LogGivingUp(string framework, int maxAttempts);

    [LoggerMessage(EventId = 7, Level = LogLevel.Critical,
        Message = "{Framework} server entry not found at '{Entry}'; stopping the application. Build the front end, or set MetaHostingOptions.AppDirectory.")]
    private partial void LogEntryMissing(string framework, string entry);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Could not start a process for {Framework}.")]
    private partial void LogStartFailed(string framework);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error,
        Message = "{Framework} did not accept a connection within {Seconds}s.")]
    private partial void LogStartupTimedOut(string framework, double seconds);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "{Framework} did not exit within {Seconds}s of SIGTERM; killing its process tree.")]
    private partial void LogGraceExpired(string framework, double seconds);
}
