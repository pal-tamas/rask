using System.Globalization;
using static Rask.Cli.Commands.ProxyCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// A lock on the box, held as a container: docker refuses a second container of one name, so taking it is atomic,
/// and the container removes itself when its lease ends, so a deploy that died holding it cannot wedge the box.
/// <list type="bullet">
/// <item>The box lock (<see cref="ForBox"/>) guards what every app shares — the network, the proxy and its config — for
/// seconds, never across a build, a start, a health gate or an old colour's drain. Its lease is fixed.</item>
/// <item>An app lock (<see cref="ForApp"/>) makes two deploys of one app take turns, for the whole deploy, through the
/// old colour finishing its jobs. A build has no upper bound, so its lease is renewed by a heartbeat while it is held
/// and runs out only after the holder stops.</item>
/// </list>
/// </summary>
internal sealed class DeployLock
{
    private readonly IConsole _console;
    private readonly IProcessRunner _process;
    private readonly string _workingDirectory;
    private readonly string _container;
    private readonly string _waitingFor;
    private readonly Func<string, string, IReadOnlyList<string>> _take;
    private readonly bool _heartbeat;
    private readonly LockTiming _timing;

    private DeployLock(
        IConsole console, IProcessRunner process, string workingDirectory, string container, string waitingFor, Func<string, string, IReadOnlyList<string>> take, bool heartbeat, LockTiming timing)
    {
        _timing = timing;
        _console = console;
        _process = process;
        _workingDirectory = workingDirectory;
        _container = container;
        _waitingFor = waitingFor;
        _take = take;
        _heartbeat = heartbeat;
    }

    internal static DeployLock ForBox(IConsole console, IProcessRunner process, string workingDirectory, LockTiming timing) =>
        new(console, process, workingDirectory, LockContainer, "switching the proxy on this box", BuildLockArguments, heartbeat: false, timing);

    internal static DeployLock ForApp(IConsole console, IProcessRunner process, string workingDirectory, string slug, LockTiming timing) =>
        new(console, process, workingDirectory, AppLockContainer(slug), $"deploying '{slug}' to this box", (host, owner) => BuildAppLockArguments(host, slug, owner), heartbeat: true, timing);

    /// <summary>Take the lock for <paramref name="slug"/>'s deploy, waiting while another deploy holds it; <c>null</c> (said why) when it can't be had.</summary>
    internal async Task<HeldLock?> AcquireAsync(string host, string slug, CancellationToken cancellationToken)
    {
        var owner = $"{slug} from {Environment.MachineName} at {DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)}";
        var holder = string.Empty;
        for (var attempt = 0; attempt < _timing.Attempts; attempt++)
        {
            if (attempt > 0 && _timing.PollDelay > TimeSpan.Zero)
            {
                await Task.Delay(_timing.PollDelay, cancellationToken).ConfigureAwait(false);
            }

            var taken = await Capture(_take(host, owner), cancellationToken).ConfigureAwait(false);
            if (taken.ExitCode == 0)
            {
                // The id, not the name, is what releases it: see BuildUnlockArguments.
                var id = taken.StandardOutput.Trim();
                return Held(host, id.Length > 0 ? id : _container);
            }

            if (!IsHeld(taken.StandardError))
            {
                _console.WriteErrorLine($"Could not take the deploy lock {_container} on {host}: {FirstLine(taken.StandardError)}", ConsoleStyle.Error);
                return null;
            }

            if (holder.Length == 0)
            {
                holder = (await Capture(BuildLockOwnerArguments(host, _container), cancellationToken).ConfigureAwait(false)).StandardOutput.Trim();
                _console.WriteLine($"  Another deploy is {_waitingFor} ({Describe(holder)}) — waiting for it…", ConsoleStyle.Dim);
            }
        }

        _console.WriteErrorLine($"Gave up waiting for the deploy lock {_container} on {host}, held by {Describe(holder)}.", ConsoleStyle.Error);
        _console.WriteErrorLine($"If no deploy is running, free it: docker -H ssh://{host} rm -f {_container}", ConsoleStyle.Error);
        return null;
    }

    /// <summary>Release a lock this deploy took. Not cancellable: a cancelled deploy must still let go of the box.</summary>
    internal async Task ReleaseAsync(string host, HeldLock held)
    {
        await held.StopHeartbeatAsync().ConfigureAwait(false);
        var released = await Capture(BuildUnlockArguments(host, held.Id), CancellationToken.None).ConfigureAwait(false);
        if (released.ExitCode != 0)
        {
            _console.WriteErrorLine($"Releasing the deploy lock {_container} failed ({FirstLine(released.StandardError)}) — it frees itself when its lease runs out.", ConsoleStyle.Warning);
        }
    }

    private HeldLock Held(string host, string id)
    {
        if (!_heartbeat || _timing.Heartbeat <= TimeSpan.Zero)
        {
            return new HeldLock(id, null, Task.CompletedTask);
        }

        var stop = new CancellationTokenSource();
        return new HeldLock(id, stop, BeatAsync(host, id, stop.Token));
    }

    // Renew the lease until released. A missed beat is only a warning: the lease outlasts several of them.
    private async Task BeatAsync(string host, string id, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_timing.Heartbeat, stop).ConfigureAwait(false);
                var beat = await Capture(BuildLockHeartbeatArguments(host, id), stop).ConfigureAwait(false);
                if (beat.ExitCode != 0)
                {
                    _console.WriteErrorLine($"  ! Renewing the deploy lock {_container} failed ({FirstLine(beat.StandardError)}).", ConsoleStyle.Warning);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Released.
        }
    }

    // docker run with a name in use: "Conflict. The container name "/rask-deploy-lock" is already in use by container …".
    private static bool IsHeld(string error) =>
        error.Contains("is already in use", StringComparison.Ordinal) || error.Contains("Conflict", StringComparison.Ordinal);

    private static string Describe(string holder) => holder.Length > 0 ? holder : "a deploy that didn't say who it is";

    private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.CaptureAsync("docker", args, _workingDirectory, cancellationToken);
}
