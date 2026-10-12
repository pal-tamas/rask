using System.Diagnostics;

namespace Rask.Core.Live;

internal sealed class HandlerSyncContext : SynchronizationContext
{
    private readonly List<Task> _pending = new();
    private readonly Func<Task> _render;

    // The moment (Stopwatch ticks) the handler's repaints stop being withheld. Zero when none are.
    private readonly long _repaintsFrom;

    /// <summary>A context whose continuations are each repainted before and after they run.</summary>
    /// <param name="render">The repaint.</param>
    /// <param name="repaintsFrom">
    ///     Until this moment (<see cref="Stopwatch.GetTimestamp" />) no continuation is repainted: a handler
    ///     that finishes before it shows one frame, its last. Zero repaints from the start.
    /// </param>
    public HandlerSyncContext(Func<Task> render, long repaintsFrom = 0)
    {
        _render = render;
        _repaintsFrom = repaintsFrom;
    }

    public override SynchronizationContext CreateCopy() => new HandlerSyncContext(_render, _repaintsFrom);

    private bool Withheld => _repaintsFrom != 0 && Stopwatch.GetTimestamp() < _repaintsFrom;

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
                    return;
                }
            }

            await Task.WhenAll(snapshot).ConfigureAwait(false);
        }
    }

    private async Task RunWithRendersAsync(SendOrPostCallback d, object? state)
    {
        await Repaint().ConfigureAwait(false);
        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }

        await Repaint().ConfigureAwait(false);
    }
}
