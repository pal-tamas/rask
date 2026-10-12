using System.Runtime.ExceptionServices;

namespace Rask.Core.Live;

internal sealed class HandlerSyncContext : SynchronizationContext
{
    private readonly List<Task> _pending = new();
    private readonly Func<Task> _render;

    public HandlerSyncContext(Func<Task> render) => _render = render;

    public override SynchronizationContext CreateCopy() => new HandlerSyncContext(_render);

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
            await _render().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failed = ExceptionDispatchInfo.Capture(ex);
        }

        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }

        await _render().ConfigureAwait(false);
        failed?.Throw();
    }
}
