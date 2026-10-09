using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    /// <summary>
    ///     A <see cref="System.Threading.CancellationToken" /> for this component's cancellable async
    ///     work. It is cancelled when the component is unmounted (navigation away, parent removed, or
    ///     session teardown) — and, while an event handler is running, <em>also</em> when the host
    ///     cancels that dispatch: a server-side <c>RaskServerOptions.HandlerTimeout</c> elapsing, or the
    ///     WebSocket closing. Pass it into the <c>HttpClient</c> calls, <c>Task.Delay</c>s, and other
    ///     cancellable work an <c>OnClick</c> / <c>OnAnySubmit</c> handler or a lifecycle hook starts, so the
    ///     work aborts when the component goes away and a slow handler unwinds instead of pinning the
    ///     session's render pipeline. In a lifecycle hook (no handler dispatch) it is just the lifetime
    ///     token. Cancellation is cooperative — synchronous or token-ignoring handler code cannot be
    ///     forcibly aborted; a handler must observe the token to be cancelled.
    /// </summary>
    protected CancellationToken CancellationToken
    {
        get
        {
            // While an event handler runs, the dispatch scope holds a token already linked with this
            // component's lifetime token (see TryInvokeHandlerAsync); outside one it is the raw lifetime
            // token. The scope is only pushed when a handler timeout is configured, so the common path
            // is the plain lifetime token below.
            var dispatch = DispatchEventTokenScope.Current;
            return dispatch.CanBeCanceled ? dispatch : LifetimeToken;
        }
    }

    // The raw, stable lifetime token — cancelled once on unmount. Used internally to seed the linked
    // per-dispatch token without re-entering the context-aware CancellationToken getter above.
    private CancellationToken LifetimeToken =>
        LazyInitializer.EnsureInitialized(ref Live.LifetimeCts, () => new CancellationTokenSource()).Token;

    /// <summary>
    ///     The raw lifetime token, for framework code that must outlive any one dispatch: a broadcast subscription made
    ///     inside an event handler would otherwise end at that handler's timeout (#1061).
    /// </summary>
    internal CancellationToken LifetimeTokenInternal => LifetimeToken;

    // One-shot guard for the unmount → cancel → dispose teardown. A tree mutation inside an
    // Unmount hook (e.g. clearing PersistedChildren, or re-parenting) can leave a node
    // reachable from more than one dispose pass; without this guard that node would fire
    // Unmount and the user's Dispose twice. Returns true exactly once. The lifetime CTS is
    // already idempotent (DisposeLifetimeToken nulls it via Interlocked, Cancel swallows ODE),
    // while this protects the user-visible lifecycle hooks. Disposal runs under the session render
    // lock, so a plain flag is sufficient — same threading contract as IsUnmounted.
    internal bool TryBeginDispose()
    {
        // A plain tag that never took a LiveState has nothing to tear down, once or twice: don't allocate one to
        // record that it was.
        if (_live is null && !OwnsRenderHandle)
        {
            return true;
        }

        if (Live.IsDisposed)
        {
            return false;
        }

        Live.IsDisposed = true;
        return true;
    }

    internal void CancelLifetimeToken()
    {
        // No LiveState → LifetimeToken was never accessed → no CTS to cancel (plain Elements never
        // reach here). Read the ref off LiveState only once we know it exists.
        var cts = _live is null ? null : Volatile.Read(ref _live.LifetimeCts);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // DisposeLifetimeToken won the race: a disposed token has nothing left to cancel.
        }
    }

    internal void DisposeLifetimeToken()
    {
        var cts = _live is null ? null : Interlocked.Exchange(ref _live.LifetimeCts, null);
        cts?.Dispose();
    }

    // Returns the cached Task.CompletedTask when there's nothing for the caller to await — the async
    // hook either wasn't overridden, completed synchronously, or already failed (faults logged inline).
    // The sync dispose path fire-and-forgets a still-running return via ObserveUnmountFault, while
    // the async path awaits it directly. Skipped entirely when Live.HasInitialized is false —
    // a component that never mounted has no unmount counterpart, symmetric with Mount.
    internal Task RaiseUnmount()
    {
        ForgetHandlerSlots();
        if (_live is not { HasInitialized: true })
        {
            return Task.CompletedTask;
        }

        // Set BEFORE Unmount fires so any StateHasChanged inside the hook (or
        // from in-flight async work — LifecycleSyncContext continuations from a
        // long-running Mount — that settles during/after unmount) is
        // silently swallowed instead of queuing ghost session renders against a
        // disposed component. Matches the documented "StateHasChanged() inside
        // Unmount is a no-op" contract.
        Live.IsUnmounted = true;

        Task task;
        try { task = OnUnmount(); }
        catch (Exception ex)
        {
            LogUnmountError(this, ex);
            return Task.CompletedTask;
        }

        if (task.IsCompletedSuccessfully)
        {
            return Task.CompletedTask;
        }

        if (task.IsFaulted)
        {
            LogUnmountError(this, task.Exception?.InnerException ?? task.Exception!);
            return Task.CompletedTask;
        }

        if (task.IsCanceled)
        {
            return Task.CompletedTask;
        }

        return task;
    }

    internal static void LogUnmountError(Component comp, Exception ex) =>
        RaskDiagnostics.Report(
            RaskLogLevel.Error,
            "Rask.Lifecycle",
            $"Rask unmount hook on {comp.GetType().Name} threw",
            ex);

    private void DisposeDeparted(
        HashSet<Component> alivePrev, HashSet<Component> aliveNow, Dictionary<Component, Component> parentMap)
    {
        // DisposeComponentTree recurses through PersistedChildren — so disposing a parent
        // ALSO disposes its descendants. To avoid disposing each descendant twice, only
        // dispose components whose previously-alive parent is still alive (or whose parent
        // is the root); the parent's recursion will handle the rest.
        foreach (var prev in alivePrev)
        {
            if (aliveNow.Contains(prev) || ReferenceEquals(prev, this))
            {
                continue;
            }

            // If our previous parent is also being disposed in this pass, the parent's
            // DisposeComponentTree will cover us — skip to avoid double-dispose.
            if (parentMap.TryGetValue(prev, out var parent) &&
                !aliveNow.Contains(parent) &&
                !ReferenceEquals(parent, this))
            {
                continue;
            }

            ComponentLifecycle.DisposeComponentTree(prev);
        }
    }

    // Disposes EditContexts present in `previous` (last frame's set) whose instance no longer
    // appears in `current` (this frame's set) — the forms they back were unmounted. Compares by
    // reference identity because one EditContext is shared across a Form's many model keys, so a
    // context survives if ANY current key still references it. No-ops when there's nothing to do.
    private static void DisposeUnmountedEditContexts(
        Dictionary<LiveRenderContext.ObjectKey, EditContext> previous,
        Dictionary<LiveRenderContext.ObjectKey, EditContext> current)
    {
        if (previous.Count == 0)
        {
            return;
        }

        var survivors = new HashSet<EditContext>(ReferenceEqualityComparer.Instance);
        foreach (var ctx in current.Values)
        {
            survivors.Add(ctx);
        }

#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var ctx in previous.Values)
#pragma warning restore S3267
        {
            if (!survivors.Contains(ctx))
            {
                ctx.Dispose();
            }
        }
    }
}
