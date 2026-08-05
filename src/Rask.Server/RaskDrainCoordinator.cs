namespace Rask.Server;

/// <summary>
///     The per-host shutdown state the live subsystem drains against — pure state, no dependencies, so
///     the store can ask it "am I draining?" without a construction cycle. One singleton per host (like
///     <see cref="RaskServerLimits" />), so two hosts in one process — or parallel tests — never observe
///     each other's shutdown. <see cref="RaskDrainService" /> owns the routine that drives it.
///     <para>
///         <b>Why the abort moved.</b> What shipped before registered <c>ws.Abort()</c> directly on the
///         host's <c>ApplicationStopping</c>, which made the hard kill the <em>first</em> move of every
///         shutdown: no close frame, so every browser saw an abnormal 1006 closure and no client could
///         tell a redeploy from a crash. <see cref="HardStopping" /> is that same abort, moved to the end
///         of the budget where a backstop belongs.
///     </para>
/// </summary>
internal sealed class RaskDrainCoordinator : IDisposable
{
    private readonly CancellationTokenSource _hardStopping = new();

    // Completed once every tracked socket loop has returned. Single-shot: the drain awaits it exactly
    // once at shutdown, so a count that dips to zero earlier in the host's life (every client
    // disconnected for a moment) completing it early is harmless — by the time it is awaited, admission
    // is already closed and no new loop can start.
    private readonly TaskCompletionSource _socketsDrained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _activeSockets;
    private volatile bool _draining;

    /// <summary>
    ///     True once the drain has begun. Gates admission (<see cref="LiveSessionStore.TryCreate" />
    ///     refuses) and readiness (<c>RaskReadinessHealthCheck</c> reports unhealthy), so a proxy or load
    ///     balancer stops routing at a host that is on its way out.
    /// </summary>
    public bool IsDraining => _draining;

    /// <summary>
    ///     Trips when the drain budget is spent. Every live socket's cancellation token derives from this
    ///     — <em>not</em> from <c>ApplicationStopping</c> — which is what lets the drain send a shutdown
    ///     frame and finish in-flight handlers on a socket the host has already been asked to stop.
    /// </summary>
    public CancellationToken HardStopping => _hardStopping.Token;

    /// <summary>Socket loops still running. Metered at the drain deadline.</summary>
    public int ActiveSockets => Volatile.Read(ref _activeSockets);

    public void Dispose() => _hardStopping.Dispose();

    /// <summary>Marks the host as draining. Idempotent.</summary>
    public void BeginDrain() => _draining = true;

    /// <summary>
    ///     Arms the backstop: every socket still open when <paramref name="budget" /> elapses is aborted.
    ///     Armed from the synchronous <c>ApplicationStopping</c> callback so it holds even if
    ///     <see cref="RaskDrainService.StopAsync" /> never runs (or runs far too late — hosted services
    ///     stop in reverse registration order, and <c>AddRask</c> is typically the first line of
    ///     <c>Program.cs</c>, so Rask stops after every battery that was registered below it).
    /// </summary>
    public void ArmDeadline(TimeSpan budget)
    {
        try
        {
            _hardStopping.CancelAfter(budget);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Trips the deadline now, aborting any socket still open. Safe to call more than once.</summary>
    public void TripDeadline()
    {
        try
        {
            _hardStopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The host disposed the container while the drain was still unwinding. Benign: the signal
            // has no one left to reach.
        }
    }

    /// <summary>
    ///     Tracks one running socket loop for the duration of the returned scope. The drain waits on
    ///     these rather than on <c>LiveSession</c> state, because the loop detaches its session
    ///     <em>before</em> it finishes unwinding — so a session-based wait would report "done" while the
    ///     close handshake was still in flight, which is the one thing the drain exists to complete.
    /// </summary>
    public IDisposable TrackSocket()
    {
        Interlocked.Increment(ref _activeSockets);
        return new SocketScope(this);
    }

    /// <summary>Completes when no socket loop is running. Await with a timeout — this has no budget of its own.</summary>
    public Task WhenSocketsDrained() =>
        Volatile.Read(ref _activeSockets) == 0 ? Task.CompletedTask : _socketsDrained.Task;

    private void ReleaseSocket()
    {
        if (Interlocked.Decrement(ref _activeSockets) == 0)
        {
            _socketsDrained.TrySetResult();
        }
    }

    private sealed class SocketScope(RaskDrainCoordinator owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            // `using` runs once, but making a double dispose harmless is cheaper than relying on every
            // caller — a negative count would make WhenSocketsDrained never complete.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.ReleaseSocket();
            }
        }
    }
}
