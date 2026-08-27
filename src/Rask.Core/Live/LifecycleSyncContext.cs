namespace Rask.Core.Live;

internal sealed class LifecycleSyncContext : SynchronizationContext
{
    private readonly Component _component;

    public LifecycleSyncContext(Component component) => _component = component;

    // Set the first time Post runs. InvokeAsyncLifecycleWithRendering's terminal
    // ContinueWith reads this to suppress its own StateHasChanged when Post already
    // fired one for the in-method await — otherwise the Post path and the terminal
    // ContinueWith both render back-to-back whenever the last statement of the user
    // hook is an await.
    internal bool PostFired { get; private set; }

    // CreateCopy is invoked along ExecutionContext capture paths; our Post explicitly
    // SuppressFlow()s ExecutionContext propagation, so the runtime never reaches this
    // along the await chains we care about. A fresh copy with PostFired=false is safe.
    public override SynchronizationContext CreateCopy() => new LifecycleSyncContext(_component);

    public override void Post(SendOrPostCallback d, object? state)
    {
        // Set before scheduling Task.Run: the user's continuation can transition the
        // hook's Task to Completed inline, which inline-fires the terminal ContinueWith
        // BEFORE we return to the line that calls StateHasChanged below. The terminal
        // callback reads PostFired to decide whether to skip — so the flag must already
        // be true by the time d(state) runs.
        PostFired = true;

        // Read HERE rather than caching it in a field. This type is allocated for every component's
        // OnMountAsync/OnPropsChangedAsync, no-op overrides included, so a field would cost eight
        // bytes on every component in the tree — measurably (+3.1 KB on the render-once pin) to
        // serve a path most components never take. Post runs under the hook's captured
        // ExecutionContext, so the AsyncLocal is still correct on this side of the suppression
        // below; only the Task.Run body needs it restored explicitly.
        var quiescence = QuiescenceScope.Current;

        // Suppress ExecutionContext flow so the continuation thread does NOT inherit
        // LiveSession.InHandlerScope=true. If it did, RequestRenderAsync() would render
        // inline without acquiring the lock — re-entering an in-progress render.
        using (ExecutionContext.SuppressFlow())
        {
            Task.Run(() =>
            {
                // Restore ONLY the quiescence scope, never the whole ExecutionContext: the
                // suppression above exists to keep InHandlerScope from crossing, and undoing it
                // would reintroduce the lock-bypassing re-entrant render it was added to prevent.
                using var scope = QuiescenceScope.Enter(quiescence);
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
