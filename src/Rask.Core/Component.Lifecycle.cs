using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    /// <summary>
    ///     Runs once, before this component first renders — load what it shows:
    ///     <c>protected override async Task OnMount() =&gt; _items = await Cache.Remember("catalog", Load).For(5.Minutes);</c>
    /// </summary>
    /// <remarks>
    ///     Everything before the first <c>await</c> runs before the first render; the component renders again
    ///     when the rest completes, with nothing to call. A server page waits for it before answering, so the
    ///     first HTML already carries the data. Static calls inside it are cancelled with this component.
    ///     A body with no <c>await</c> is written <c>async</c> all the same; the compiler no longer warns about it.
    /// </remarks>
    protected virtual Task OnMount() => Task.CompletedTask;

    /// <summary>
    ///     Runs when a parent passes this component new values — and once on mount, so a component whose data
    ///     depends on a prop needs only this: <c>protected override async Task OnUpdated() =&gt; _product = await Product.Where(p =&gt; p.Id == Id).First();</c>
    /// </summary>
    /// <remarks>Not for this component's own state changes, which simply render again.</remarks>
    protected virtual Task OnUpdated() => Task.CompletedTask;

    /// <summary>
    ///     Runs once, after this component is first in the page — where browser work that needs its elements
    ///     starts: <c>protected override async Task OnFirstRender() =&gt; _watch = await resize.Observe(_box, OnResize);</c>
    /// </summary>
    protected virtual Task OnFirstRender() => Task.CompletedTask;

    /// <summary>
    ///     Runs after every render, the first included (after <see cref="OnFirstRender" />) — to keep something
    ///     outside Rask in step with what was just rendered.
    /// </summary>
    protected virtual Task OnRendered() => Task.CompletedTask;

    /// <summary>
    ///     Runs once when this component leaves the tree — navigation away, its parent's subtree torn down, or
    ///     the session ending. Symmetric with <see cref="OnMount" />.
    /// </summary>
    /// <remarks>
    ///     The component's <see cref="CancellationToken" /> is still live here and cancelled right after. Awaited
    ///     on asynchronous teardown (a session disposing); on a synchronous one it runs on, and a fault is logged.
    ///     <see cref="StateHasChanged()" /> inside it does nothing — the component is leaving.
    /// </remarks>
    protected virtual Task OnUnmount() => Task.CompletedTask;

    /// <summary>
    ///     Whether this component has left the tree. Read by <c>QuiescenceScope</c> so a server
    ///     render stops waiting on work whose owner was unmounted mid-pass.
    /// </summary>
    internal bool IsUnmountedInternal => Live.IsUnmounted;

    /// <summary>
    ///     The render pass this root is in, as stamped by <see cref="BeginHandlerGeneration" />; zero on a
    ///     component that has never been a render root. Read without allocating the live state.
    /// </summary>
    internal long RenderGenerationInternal => _live?.HandlerState?.Generation ?? 0;

    /// <summary>
    ///     Runs <paramref name="hook" /> each time this component's <c>Render()</c> returns, inside the
    ///     render context — for state kept per render that must drop what a render stopped asking for.
    /// </summary>
    internal void AddAfterRenderInternal(Action hook) => Live.AfterRender += hook;

    /// <summary>
    ///     Whether the last render walk rooted here mounted <paramref name="type" />. The set is
    ///     cleared at the top of every walk and populated as components mount, so this describes
    ///     the render whose HTML is current — not the tree's history.
    /// </summary>
    internal bool MountedTypeInLastRender(Type type) =>
        Live.MountedTypes?.Contains(type) == true;

    internal void RaiseLifecycleBeforeRender(bool propsChanged)
    {
        var firstRender = !Live.HasInitialized;
        if (firstRender)
        {
            Live.HasInitialized = true;
            InvokeAsyncLifecycleWithRendering(OnMount);
        }

        if (firstRender || propsChanged)
        {
            Live.PropsDirty = true;
            InvokeAsyncLifecycleWithRendering(OnUpdated);
        }
    }

    internal void RaiseOnRendered(bool publishOnly = false)
    {
        // publishOnly: this is the render walk triggered by a previous OnRendered
        // continuation's auto-rerender. Skip OnRendered on components
        // that already rendered at least once — re-entering the hook would re-await
        // whatever it awaits (e.g. js.InvokeVoidAsync), enqueue another pending task,
        // schedule another publish render, complete → loop. First-time renders still
        // fire so newly-mounted components on the same walk get their first
        // OnRendered(firstRender:true) — they don't have a prior continuation in flight,
        // so they can't loop.
        var firstRender = !GetLifecycleFlag(FlagHasRenderedOnce);
        if (publishOnly && !firstRender)
        {
            return;
        }

        SetLifecycleFlag(FlagHasRenderedOnce, true);
        // Called directly rather than through a Func: this runs for every component on every render, and a
        // method-group delegate would allocate each time.
        if (firstRender)
        {
            AfterRendered(OnFirstRender());
        }

        AfterRendered(OnRendered());
    }

    private void AfterRendered(Task task)
    {
        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        // Auto-rerender on continuation completion so users get Mount-style
        // "mutate state after the await and it paints" without explicit StateHasChanged.
        // RequestPublishRender flags the resulting walk as publishOnly so the
        // publish render skips this same hook on every already-rendered component (see
        // top of method). Without that flag, multi-component trees cascade infinitely:
        // A's publish render fires B's OnRendered, B's continuation publishes,
        // which fires A's OnRendered again, ad infinitum.
        // Discarded on purpose: the continuation IS the work, and nothing awaits it (RASK093).
        _ = task.ContinueWith(static (t, state) =>
        {
            var comp = (Component)state!;
            if (t.IsFaulted)
            {
                ReportLifecycleFault(comp, t.Exception);
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            if (comp.Live.IsUnmounted)
            {
                return;
            }

            var handle = comp.RenderHandle;
            if (handle is null)
            {
                return;
            }

            comp.Live.StateDirty = true;
            _ = handle.RequestPublishRender();
        }, this, TaskContinuationOptions.ExecuteSynchronously);
    }

    // A scoped-script callback arriving from the browser (ScopedScript.Callback): runs like a lifecycle
    // hook — a render after each await — and a synchronous one paints once it returns, which a hook does
    // not need because the render walk that called it is already painting.
    //
    // It runs in order with the session's event handlers (IRenderHandle.RunInOrder), so a timer's callback cannot
    // change state underneath a click being handled.
    internal void RunFromScript(Func<Task?> invoke)
    {
        if (IsTornDown)
        {
            return;
        }

        if (RenderHandle is { } handle)
        {
            handle.RunInOrder(() => RunScriptCallback(invoke));
        }
        else
        {
            _ = RunScriptCallback(invoke);
        }
    }

    // The same, run at once and finished when the callback is: for one the browser awaits (a lock's), whose caller is
    // itself awaiting the browser, so queueing it in order would wait on that caller forever.
    internal Task RunFromScriptNow(Func<Task?> invoke) => IsTornDown ? Task.CompletedTask : RunScriptCallback(invoke);

    /// <summary>Unmounted or disposed — what a script-side registration made now would never be released from.</summary>
    internal bool IsTornDown => _live is { IsUnmounted: true } or { IsDisposed: true };

    private Task RunScriptCallback(Func<Task?> invoke)
    {
        if (IsTornDown)
        {
            return Task.CompletedTask;
        }

        Task? running = null;
        InvokeAsyncLifecycleWithRendering(() =>
        {
            try { running = invoke(); }
            catch (Exception ex) { running = Task.FromException(ex); }

            if (running is null || running.IsCompletedSuccessfully)
            {
                StateHasChanged();
                return Task.CompletedTask;
            }

            return running;
        });

        return running ?? Task.CompletedTask;
    }

    private void InvokeAsyncLifecycleWithRendering(Func<Task> invoke)
    {
        var prev = SynchronizationContext.Current;

        // Resolved HERE, on the render walk, and handed to the context — because this is the only
        // place it is knowable. The walk runs inside the pass's own flow, so the AsyncLocal is
        // exact; LifecycleSyncContext.Post runs on whichever thread finished the awaited task,
        // where it is not (see the field it is stored in). One lookup now serves both the context
        // and the Track below, which used to do its own.
        var quiescence = QuiescenceScope.Current;
        var ctx = new LifecycleSyncContext(this, quiescence);
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task task;
        try { task = invoke(); }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        var painted = task.ContinueWith(PaintAfterHook, (this, ctx), TaskContinuationOptions.ExecuteSynchronously);

        // Tracked on the PAINT, never on the hook's own Task — the ordering guarantee the wave loop
        // rests on, and the second half of #932's fix (#1037).
        //
        // The hook's Task completes one statement before the continuation above requests the render
        // that shows its data, and both continuations run ExecuteSynchronously, so tracking the hook
        // directly published a task that completed inside the window it exists to close: the loop
        // could wake on it, re-render the still-clean child, find nothing pending and serve the
        // placeholder at 200 with nothing marked timed out. Post closes that window for itself with
        // an explicit gate — but Post never runs for a hook whose awaits are all ConfigureAwait(false),
        // which is what library code is normally advised to write. Chaining the tracked wrapper onto
        // the terminal continuation closes it for BOTH paths, and costs nothing: this is the same two
        // continuations as before, in series rather than in parallel.
        //
        // Order here is safe either way. If the hook completes before this line, the continuation
        // above has already run inline and requested the render, so what gets registered is a
        // settled task and the loop simply takes one more wave over correct state.
        quiescence?.Track(painted, this);
    }

    // LifecycleSyncContext renders after each in-method await. The terminal render
    // here is the fallback for hooks that return a Task without awaiting it AND for
    // ConfigureAwait(false)-only chains where Post never fires. When the user's last
    // statement IS an await (the common case), Post already fired StateHasChanged
    // for it — and the user's method body returns inside d(state), transitioning the
    // task to Completed while still inside the Post lambda. ExecuteSynchronously
    // would then fire THIS callback inline before Post's own StateHasChanged runs,
    // producing two renders back-to-back. ctx.PostFired lets us short-circuit in
    // that case.
    private static void PaintAfterHook(Task t, object? state)
    {
#pragma warning disable S8969 // the compiler needs it: unboxing an object? is CS8605 without it
        var (comp, ctx) = ((Component, LifecycleSyncContext))state!;
#pragma warning restore S8969
        if (t.IsFaulted)
        {
            ReportLifecycleFault(comp, t.Exception);
            return;
        }

        if (t.IsCanceled || ctx.PostFired)
        {
            return;
        }

        comp.StateHasChanged();
    }

    private static ErrorBoundary? ResolveHandlerBoundary(Component owner) =>
        owner as ErrorBoundary ?? owner.Boundary;

    // A faulted async lifecycle hook reaches its boundary through the task, not through a catch around the user's
    // code, so a debugger never reports it — unlike a handler's exception (see TryInvokeHandlerAsync), which it
    // stops on by itself. The attribute says this method is not the user handling the exception, and the
    // BreakForUserUnhandledException call asks the debugger to stop with it in hand; its stack still names the
    // throw line. Verified under VS Code's F5: this is the only stop such a fault produces. Standard .NET API,
    // doing nothing without a debugger attached.
    [System.Diagnostics.DebuggerDisableUserUnhandledExceptions]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportLifecycleFault(Component comp, AggregateException? ex)
    {
        var actual = ex?.InnerException ?? ex;
        if (actual is null)
        {
            return;
        }

        // A component that has left the page has nothing to show an error on, and its boundary now holds the
        // page the visitor went to. Leaving cancels its load, and a provider may report that cancellation as a
        // failure of its own (EF's execution strategy does), so this is ordinary: recorded, never displayed.
        if (comp.IsTornDown)
        {
            RaskDevToolsHook.Active?.ComponentFaulted(comp, actual, ErrorSource.Lifecycle, caught: false);
            RaskDiagnostics.Report(
                RaskLogLevel.Information,
                "Rask.Lifecycle",
                $"Rask lifecycle hook on {comp.GetType().Name} faulted after it left the page",
                actual);
            return;
        }

        // Prefer the boundary: it'll re-render with the fallback. Fall back to a diagnostics
        // report only when there is no ancestor boundary, so a faulting hook is never silent.
        var boundary = comp.Boundary;
        if (boundary is not null)
        {
            System.Diagnostics.Debugger.BreakForUserUnhandledException(actual);
            RaskDevToolsHook.Active?.ComponentFaulted(comp, actual, ErrorSource.Lifecycle, caught: true);
            boundary.Trip(actual, ErrorSource.Lifecycle);
            return;
        }

        RaskDevToolsHook.Active?.ComponentFaulted(comp, actual, ErrorSource.Lifecycle, caught: false);
        RaskDiagnostics.Report(
            RaskLogLevel.Error,
            "Rask.Lifecycle",
            $"Rask lifecycle hook on {comp.GetType().Name} faulted",
            actual);
    }
}
