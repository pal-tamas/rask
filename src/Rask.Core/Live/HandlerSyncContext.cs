using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Rask.Core.Live;

internal sealed class HandlerSyncContext : SynchronizationContext
{
    private readonly List<Task> _pending = new();
    private readonly Func<Task> _render;

    // The moment (Stopwatch ticks) the handler's repaints stop being withheld. Zero when none are.
    private readonly long _repaintsFrom;

    // Shared with every copy, as the moment is.
    private readonly Opening _opening;

    /// <summary>A context whose continuations are each repainted before and after they run.</summary>
    /// <param name="render">The repaint.</param>
    /// <param name="repaintsFrom">
    ///     Until this moment (<see cref="Stopwatch.GetTimestamp" />) no continuation is repainted: a handler
    ///     that finishes before it shows one frame, its last. Zero repaints from the start.
    /// </param>
    public HandlerSyncContext(Func<Task> render, long repaintsFrom = 0)
        : this(render, repaintsFrom, new Opening())
    {
    }

    private HandlerSyncContext(Func<Task> render, long repaintsFrom, Opening opening)
    {
        _render = render;
        _repaintsFrom = repaintsFrom;
        _opening = opening;
    }

    public override SynchronizationContext CreateCopy() => new HandlerSyncContext(_render, _repaintsFrom, _opening);

    /// <summary>
    ///     The budget is spent and its repaint is being made: from here every continuation is repainted, whatever
    ///     the clock says.
    /// </summary>
    /// <remarks>
    ///     The budget is timed by a timer, the moment by <see cref="Stopwatch" />, and the timer can fire a
    ///     fraction of a millisecond before the moment. A continuation that came back in that gap — the browser
    ///     answering the budget's frame at once — had both its repaints withheld: the call it went on to queue
    ///     never left, and the handler waited on it for good.
    /// </remarks>
    public void OpenRepaints() => _opening.Open = true;

    private bool Withheld => _repaintsFrom != 0 && !_opening.Open && Stopwatch.GetTimestamp() < _repaintsFrom;

    private Task Repaint() => Withheld ? Task.CompletedTask : _render();

    public override void Post(SendOrPostCallback d, object? state)
    {
        // Schedule AND record under the same lock so DrainAsync can't snapshot _pending in the
        // gap between Task.Run and the Add — which could otherwise let the drain return before a
        // just-posted render completes. Task.Run always schedules on the pool (never inline), so
        // holding the lock around it doesn't run user code under the lock.
        lock (_pending)
        {
            _pending.Add(Task.Run(() => RunWithRendersAsync(d, state)));
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
        => RunWithRendersAsync(d, state).GetAwaiter().GetResult();

    public async Task DrainAsync()
    {
        while (true)
        {
            Task[] snapshot;
            lock (_pending)
            {
                snapshot = _pending.Where(t => !t.IsCompleted).ToArray();
                if (snapshot.Length == 0)
                {
                    break;
                }
            }

            // Every continuation is waited for, whatever became of the one before it.
            await Task.WhenAll(snapshot).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        // A repaint that failed between two awaits is the handler's fault, once all of its code has run.
        Task? failed;
        lock (_pending)
        {
            failed = _pending.Find(t => !t.IsCompletedSuccessfully);
        }

        if (failed is not null)
        {
            await failed.ConfigureAwait(false);
        }
    }

    private async Task RunWithRendersAsync(SendOrPostCallback d, object? state)
    {
        // The repaint before a continuation may throw: a frame lost as the connection dropped, a fault no error
        // boundary took. The continuation runs all the same — skipped, the handler never finished and held its
        // session for good — and the fault is thrown once it has.
        ExceptionDispatchInfo? failed = null;
        try
        {
            await Repaint().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failed = ExceptionDispatchInfo.Capture(ex);
        }

        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }

        await Repaint().ConfigureAwait(false);
        failed?.Throw();
    }

    // Written by the handler's thread, read by its continuations'.
    private sealed class Opening
    {
        private volatile bool _open;

        public bool Open
        {
            get => _open;
            set => _open = value;
        }
    }
}
