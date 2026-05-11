namespace Rask.Core.Live;

internal sealed class LifecycleSyncContext : SynchronizationContext
{
    private readonly Component _component;

    public LifecycleSyncContext(Component component) => _component = component;

    public override SynchronizationContext CreateCopy() => new LifecycleSyncContext(_component);

    public override void Post(SendOrPostCallback d, object? state)
    {
        // Suppress ExecutionContext flow so the continuation thread does NOT inherit
        // LiveSession.InHandlerScope=true. If it did, RequestRenderAsync() would render
        // inline without acquiring the lock — re-entering an in-progress render.
        using (ExecutionContext.SuppressFlow())
        {
            Task.Run(() =>
            {
                var prev = Current;
                SetSynchronizationContext(this);
                try { d(state); }
                finally { SetSynchronizationContext(prev); }

                _component.StateHasChanged();
            });
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
