using System.Globalization;
using Microsoft.Extensions.Hosting;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Server.Diagnostics;

namespace Rask.Server;

/// <summary>
///     Drains the live sessions on a graceful shutdown: announce, let in-flight handlers finish, close
///     each socket with a real handshake, dispose awaited. Registered by <c>AddRask</c>; there is nothing
///     to opt into.
///     <para>
///         <b>Why the work is split across two moments.</b> Hosted services stop in reverse registration
///         order, and <c>AddRask</c> is typically the first line of <c>Program.cs</c> — so this service's
///         <see cref="StopAsync" /> runs <em>after</em> every battery registered below it, and a
///         Litestream flush can have spent most of <c>HostOptions.ShutdownTimeout</c> before we are even
///         entered. Anything that must happen at t=0 therefore happens in the synchronous
///         <c>ApplicationStopping</c> callback registered by <see cref="StartAsync" />: closing admission,
///         flipping readiness, arming the backstop, and starting the announcement. Only the parts that
///         must be <em>awaited</em> wait for <see cref="StopAsync" />, which the host does block on.
///     </para>
/// </summary>
internal sealed class RaskDrainService : IHostedService, IDisposable
{
    // Ceiling on the post-deadline teardown pass. Disposal is local work (unmount hooks, DI scope
    // disposal) with every socket already aborted, so this is a wedge-guard, not a budget.
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(2);

    private readonly RaskDrainCoordinator _coordinator;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly RaskServerLimits _limits;
    private readonly RaskMetrics? _metrics;
    private readonly LiveSessionStore _store;

    private Task _announce = Task.CompletedTask;
    private CancellationTokenRegistration _stoppingRegistration;

    public RaskDrainService(
        LiveSessionStore store,
        RaskDrainCoordinator coordinator,
        RaskServerLimits limits,
        IHostApplicationLifetime? lifetime = null,
        RaskMetrics? metrics = null)
    {
        _store = store;
        _coordinator = coordinator;
        _limits = limits;
        _lifetime = lifetime;
        _metrics = metrics;
    }

    public void Dispose() => _stoppingRegistration.Dispose();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_lifetime is not null)
        {
            _stoppingRegistration = _lifetime.ApplicationStopping.Register(BeginDrain);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // StopAsync can be reached without ApplicationStopping having fired (a host stopped directly, or
        // a test that calls StopAsync on the app). Begin here too so the drain is never skipped.
        BeginDrain();

        if (_limits.ShutdownDrainTimeout <= TimeSpan.Zero)
        {
            return;
        }

        using var budget = new CancellationTokenSource(_limits.ShutdownDrainTimeout);
        var token = budget.Token;

        try
        {
            // Bounded by the drain budget like every other step. Un-bounded, a client that has stopped
            // reading TCP holds the announcement for the whole per-send SendTimeout (30s by default) —
            // longer than the drain budget, longer than HostOptions.ShutdownTimeout, and long enough for
            // `rask deploy`'s SIGKILL to land in the middle of a SQLite checkpoint. Anyone who misses
            // their frame simply reconnects the old way, which is a far cheaper loss.
            await SwallowAsync(_announce.WaitAsync(token)).ConfigureAwait(false);
            await SettleHandlersAsync(token).ConfigureAwait(false);
            await CloseSocketsAsync(token).ConfigureAwait(false);
            await SwallowAsync(_coordinator.WhenSocketsDrained().WaitAsync(token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Budget spent. Fall through: the abandoned count is reported below and the deadline is
            // tripped either way, so a wedged handler costs the budget rather than the shutdown.
        }

        var abandoned = _coordinator.ActiveSockets;

        // Trip the deadline BEFORE the dispose loop, not after. A session whose handler still holds the
        // render lock would otherwise block DisposeAsync indefinitely; aborting the socket makes its
        // in-flight send fail, the render unwinds, and the lock is released.
        _coordinator.TripDeadline();
        await DisposeSessionsAsync().ConfigureAwait(false);

        if (abandoned > 0)
        {
            _metrics?.SessionsAbandonedAtDrain(abandoned);
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Rask: {abandoned} live session(s) were still connected when the {_limits.ShutdownDrainTimeout.TotalSeconds:0.##}s drain budget ran out; their sockets were aborted. Raise RaskServerOptions.ShutdownDrainTimeout, or HostOptions.ShutdownTimeout if it is the tighter of the two."));
        }
    }

    // Every per-session await goes through this. One session whose socket already faulted, whose handler
    // threw, or whose wait outran the budget must not stop the drain — the other sessions still need it.
    // Cancellation is an expected outcome here, not an error, so it is swallowed too; the budget is
    // enforced by the loops that own a token, and by the deadline the coordinator armed at t=0.
    private static async Task SwallowAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberate: see above.
        catch
#pragma warning restore CA1031
        {
        }
    }

    private void BeginDrain()
    {
        if (_coordinator.IsDraining)
        {
            return;
        }

        _coordinator.BeginDrain();

        if (_limits.ShutdownDrainTimeout <= TimeSpan.Zero)
        {
            // Drain disabled: restore the historical behaviour of aborting immediately.
            _coordinator.TripDeadline();
            return;
        }

        // The backstop is armed here rather than in StopAsync so it holds even if StopAsync never runs,
        // or runs so late that the budget is already gone.
        _coordinator.ArmDeadline(_limits.ShutdownDrainTimeout);

        var sessions = _store.Count;
        if (sessions > 0)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Information, "Rask.Live",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Rask: draining {sessions} live session(s) with a {_limits.ShutdownDrainTimeout.TotalSeconds:0.##}s budget."));
        }

        _announce = Task.Run(AnnounceAsync);
    }

    /// <summary>
    ///     Tells every connected browser the server is going away, so the client shows "Updating…" and
    ///     comes back where it was instead of reading the replacement process's <c>session/unknown</c>
    ///     reply as an idle timeout.
    ///     <para>
    ///         Goes through <c>LiveSessionStore.BroadcastAsync</c>, which fans out concurrently with a
    ///         bounded degree and skips a session whose send faults. Sends to <em>different</em> sessions
    ///         are safe in parallel — each owns its own lock and socket — but the bound matters most
    ///         precisely here: a host shutting down under load has a busy thread pool, and an unbounded
    ///         fan-out across thousands of sessions is the wrong thing to add to it.
    ///     </para>
    /// </summary>
    private Task AnnounceAsync() => _store.BroadcastAsync(LivePayload.ServerShutdownFrame);

    /// <summary>
    ///     Waits for in-flight handler dispatches to finish. This is the part that only became meaningful
    ///     once the socket token stopped deriving from <c>ApplicationStopping</c>: the chain now runs on a
    ///     live token instead of unwinding on a cancelled one, so awaiting it actually lets a click that
    ///     is mid-<c>SaveChangesAsync</c> complete.
    /// </summary>
    private async Task SettleHandlersAsync(CancellationToken token)
    {
        foreach (var session in _store.Snapshot())
        {
            // Poll the count before awaiting the tail: reading LastHandlerTask once can be stale, because
            // the receive loop may chain another dispatch onto it immediately after the read.
            while (session.PendingHandlers > 0)
            {
                await Task.Delay(15, token).ConfigureAwait(false);
            }

            await SwallowAsync(session.LastHandlerTask.WaitAsync(token)).ConfigureAwait(false);
        }
    }

    private Task CloseSocketsAsync(CancellationToken token) =>
        Task.WhenAll(_store.Snapshot().Select(s => SwallowAsync(s.CloseForShutdownAsync(token))));

    private async Task DisposeSessionsAsync()
    {
        // Bounded even though Detach removes before disposing (so Count strictly decreases): a component
        // whose unmount hangs would otherwise wedge shutdown here forever. The deadline was tripped just
        // above, so anything blocked on a render lock has already been unblocked.
        using var teardown = new CancellationTokenSource(TeardownTimeout);

        // A loop rather than one pass: TryCreate can have admitted a session microseconds before the
        // draining flag flipped, and that one is not in the snapshot the drain started from.
        while (_store.Count > 0 && !teardown.IsCancellationRequested)
        {
            foreach (var id in _store.SessionIds())
            {
                await SwallowAsync(_store.RemoveAsync(id).WaitAsync(teardown.Token)).ConfigureAwait(false);
            }
        }
    }
}
