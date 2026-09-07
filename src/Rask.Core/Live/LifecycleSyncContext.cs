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

        // A gate the quiescence loop can wait on, registered HERE — before anything can complete.
        //
        // The hook's own Task is not enough, and the gap is exactly one line wide. The user's method
        // body returns inside d(state) below, which transitions that Task to Completed while still
        // inside this lambda — one line BEFORE StateHasChanged() runs. QuiescentRender.RunAsync waits
        // on the hook Task, so it can wake in that window, re-render, find nothing pending and serve
        // the page. The data resolved; the render that would have shown it had not happened yet.
        //
        // What that produced was a page served with its placeholder, at 200, well inside the budget,
        // with nothing marked as timed out and no fault anywhere — a server-rendered page whose data
        // silently did not make it (#932). It reproduced as roughly one run in six under load and never
        // in isolation, because it needs this Task.Run to be scheduled late.
        //
        // Registering before Task.Run is what makes it airtight: Post always runs before the
        // continuation it schedules, so the loop cannot snapshot between the two. TrackExternal is a
        // no-op on a scope that has already been disposed.
        var rendered = quiescence is null
            ? null
            : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (rendered is not null)
        {
            quiescence!.TrackExternal(rendered.Task);
        }

        // Suppress ExecutionContext flow so the continuation thread does NOT inherit
        // LiveSession.InHandlerScope=true. If it did, RequestRenderAsync() would render
        // inline without acquiring the lock — re-entering an in-progress render.
        using (ExecutionContext.SuppressFlow())
        {
            Task.Run(() =>
            {
                try
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
                }
                finally
                {
                    // In a finally, and unconditionally: a hook that throws must not leave the initial
                    // render waiting on a gate nothing will ever open. The fault is already routed to
                    // the nearest ErrorBoundary by the caller.
                    rendered?.TrySetResult();
                }
            });
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
