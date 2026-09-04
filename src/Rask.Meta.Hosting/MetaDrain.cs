namespace Rask.Meta.Hosting;

/// <summary>
///     Whether the host is shutting down, and how many forwarded requests are still in flight.
/// </summary>
/// <remarks>
///     <para>
///         This exists because of an ordering hazard <c>Rask.Server</c>'s own drain coordinator already
///         records: <b>hosted services stop in reverse registration order</b>. Kestrel's
///         <c>GenericWebHostService</c> is registered before anything an app adds, so it stops
///         <em>last</em> — and the supervisor, registered later, stops <em>first</em>. Left alone that
///         means the Node process is killed while Kestrel is still draining, and every in-flight page
///         render becomes a 502 on every deploy.
///     </para>
///     <para>
///         So the drain is armed from the synchronous <c>ApplicationStopping</c> callback rather than
///         from <c>StopAsync</c>, for the same reason that one is: it holds regardless of where in the
///         stop order this service happens to sit.
///     </para>
///     <para>
///         Separate from <see cref="NodeReadiness" /> deliberately. That answers "is the front end
///         listening"; this answers "are we still willing to forward". Folding them together would make
///         a 503 ambiguous exactly when someone is diagnosing a deploy.
///     </para>
/// </remarks>
internal sealed class MetaDrain
{
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inFlight;
    private volatile bool _draining;

    /// <summary>True once shutdown has begun. New requests are refused rather than forwarded.</summary>
    internal bool IsDraining => _draining;

    /// <summary>Forwarded requests still running. Metered at the drain deadline.</summary>
    internal int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>Marks the host as draining. Idempotent.</summary>
    internal void BeginDrain()
    {
        _draining = true;

        // A drain that begins with nothing in flight is already finished. Without this the wait below
        // would sit out its whole budget on an idle host — which is every deploy of a small app.
        if (InFlight == 0)
        {
            _idle.TrySetResult();
        }
    }

    /// <summary>Records a forward starting.</summary>
    internal void Enter() => Interlocked.Increment(ref _inFlight);

    /// <summary>Records a forward finishing, completing the drain when it was the last one.</summary>
    internal void Exit()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0 && _draining)
        {
            _idle.TrySetResult();
        }
    }

    /// <summary>
    ///     Waits until nothing is in flight, or the budget runs out. Reports whether it drained.
    /// </summary>
    internal async Task<bool> WaitForIdleAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        if (InFlight == 0)
        {
            return true;
        }

        try
        {
            await _idle.Task.WaitAsync(budget, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
