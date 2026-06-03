namespace Rask.Core.Live;

internal sealed class HandlerSyncContext : SynchronizationContext
{
    private readonly List<Task> _pending = new();
    private readonly Func<Task> _render;

    public HandlerSyncContext(Func<Task> render) => _render = render;

    public override SynchronizationContext CreateCopy() => new HandlerSyncContext(_render);

    public override void Post(SendOrPostCallback d, object? state)
    {
        var task = Task.Run(() => RunWithRendersAsync(d, state));
        lock (_pending)
        {
            _pending.Add(task);
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
        await _render().ConfigureAwait(false);
        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }

        await _render().ConfigureAwait(false);
    }
}
