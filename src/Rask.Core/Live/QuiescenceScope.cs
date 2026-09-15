namespace Rask.Core.Live;

/// <summary>
///     Collects the async lifecycle work started by one server render so the host can wait for it
///     before serving the HTML.
/// </summary>
/// <remarks>
///     <para>
///         <c>OnMountAsync</c> is deliberately fire-and-forget: the render walk starts it, keeps
///         walking, and the continuation paints later over the live connection. That is right once
///         a socket exists and wrong for the first response, where "later" is after the bytes have
///         already gone — which is why a page that loads its data in <c>OnMountAsync</c> serves its
///         placeholder to the first paint and to every crawler.
///     </para>
///     <para>
///         Nothing tracked the hooks' tasks, so there was nothing to await. This is that registry.
///         It is ambient rather than passed down because the walk that starts the work is several
///         frames below the host that has to wait for it, and threading a parameter through every
///         render entry point would put an SSR concern into signatures that have nothing to do with
///         it.
///     </para>
///     <para>
///         Lookup is the <c>AsyncLocal</c> alone. A <c>ThreadStatic</c> beside it outlived the pass that
///         set it: <c>QuiescentRender.RunAsync</c> begins on a pool thread and awaits, so the thread
///         kept a LIVE render's scope, and a scope-less render landing there next tracked its hooks into
///         that stranger until the stranger hit its wave cap (#1108).
///     </para>
/// </remarks>
internal sealed class QuiescenceScope : IDisposable
{
    private static readonly AsyncLocal<QuiescenceScope?> _asyncCurrent = new();

    private readonly List<(Task Wrapped, Component? Owner)> _pending = new();

    // External work is registered from a property read, which happens many times per render for the
    // same task, so it is deduped by identity rather than appended each time.
    private readonly HashSet<Task> _externalSeen = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>The scope collecting work for the render currently running, if any.</summary>
    /// <remarks>
    ///     <para>
    ///         <b>Only the flow answers.</b> The <c>AsyncLocal</c> belongs to the render that is actually
    ///         running here. A thread slot cannot: after an <c>await</c> the thread goes back to the pool
    ///         still holding whatever pass began on it, and the next render there — its own or a
    ///         stranger's — would read that. The one path that loses the <c>AsyncLocal</c>,
    ///         <c>LifecycleSyncContext</c>'s suppressed <c>Task.Run</c>, restores the captured scope with
    ///         <see cref="Enter" />.
    ///     </para>
    ///     <para>
    ///         <b>Ask this only from the render walk.</b> Every caller is on it, and that is the whole
    ///         reason the answer is trustworthy: the walk runs inside the pass's own flow. A CONTINUATION
    ///         cannot ask — a <c>SynchronizationContext.Post</c> runs before the runtime restores the
    ///         awaiter's captured <c>ExecutionContext</c> (that happens around the continuation itself),
    ///         so a lookup there answers null, or a stranger, at random. Work started off the walk must
    ///         be handed a scope captured on it — see <c>LifecycleSyncContext</c>'s field. This cost #932
    ///         twice.
    ///     </para>
    ///     <para>A disposed scope is never current, even while a flow still carries it.</para>
    /// </remarks>
    internal static QuiescenceScope? Current => _asyncCurrent.Value is { _disposed: false } scope ? scope : null;

    /// <summary>
    ///     Whether any wave gave up before its work settled — the page is being served incomplete.
    /// </summary>
    /// <remarks>
    ///     The host must treat this as "force interactive". A page whose data never arrived has to
    ///     keep a live session to finish loading; served as a static document it would sit on its
    ///     placeholder for ever, with nothing left running that could replace it.
    /// </remarks>
    internal bool TimedOut { get; private set; }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _pending.Clear();
        }

        if (ReferenceEquals(_asyncCurrent.Value, this))
        {
            _asyncCurrent.Value = null;
        }
    }

    /// <summary>Open a scope and make it current for this render pass.</summary>
    internal static QuiescenceScope Begin()
    {
        var scope = new QuiescenceScope();
        _asyncCurrent.Value = scope;
        return scope;
    }

    /// <summary>
    ///     Re-establish <paramref name="captured" /> as current, for code that has
    ///     crossed an <see cref="ExecutionContext.SuppressFlow" /> boundary and so lost the
    ///     <c>AsyncLocal</c>.
    /// </summary>
    internal static IDisposable Enter(QuiescenceScope? captured) => new Restore(captured);

    /// <summary>
    ///     Record work the render depends on that no lifecycle hook returned — see
    ///     <c>LiveRenderContext.AwaitBeforeFirstPaint</c>.
    /// </summary>
    /// <remarks>
    ///     Held with no owning component, deliberately. Such work is typically shared — one cache
    ///     entry serving several readers — so dropping it when any one of them unmounts would be
    ///     wrong for the rest. The pass budget is what bounds it instead.
    /// </remarks>
    internal void TrackExternal(Task task)
    {
        lock (_lock)
        {
            if (_disposed || !_externalSeen.Add(task))
            {
                return;
            }
        }

        Track(task, owner: null);
    }

    /// <summary>
    ///     Record one piece of work this render is waiting on, along with the component that owns it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Stores a wrapper that completes when <paramref name="task" /> does but never faults or
    ///         cancels, so a batch can be awaited with a plain <c>WhenAll</c>. Faults are already routed
    ///         to the nearest <c>ErrorBoundary</c> by the caller; re-observing them here would either
    ///         throw out of the wait or double-report.
    ///     </para>
    ///     <para>
    ///         <b>Work that paints through a component's own <c>StateHasChanged</c> must not be handed in
    ///         as the hook's <c>Task</c>.</b> That Task completes one statement before the continuation
    ///         that requests the render, and the wave loop is free to wake in between, re-render a
    ///         component that is still clean, find nothing pending and serve its placeholder at 200. So
    ///         <c>Component.InvokeAsyncLifecycleWithRendering</c> hands in its terminal continuation
    ///         rather than the hook, and <c>LifecycleSyncContext.Post</c> hands in a gate it opens only
    ///         after its own repaint. This cost #932 and #1037.
    ///     </para>
    /// </remarks>
    internal void Track(Task task, Component? owner)
    {
        var wrapped = task.ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _pending.Add((wrapped, owner));
        }
    }

    /// <summary>
    ///     Take everything recorded since the last call. Returns <c>false</c> when the render has
    ///     settled and no further wave is needed.
    /// </summary>
    /// <remarks>
    ///     Work owned by a component that has since left the tree is dropped rather than awaited: a
    ///     placeholder replaced by the data it was waiting for is unmounted mid-pass, and its
    ///     abandoned fetch would otherwise hold the whole response open for the full budget.
    /// </remarks>
    internal bool TrySnapshotPending(out Task[] batch)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                batch = [];
                return false;
            }

            var live = new List<Task>(_pending.Count);
            foreach (var (wrapped, owner) in _pending)
            {
                // A null owner is unowned work (see TrackExternal) and always counts as live.
                if (owner is null || !owner.IsUnmountedInternal)
                {
                    live.Add(wrapped);
                }
            }

            _pending.Clear();
            batch = live.ToArray();
            return batch.Length > 0;
        }
    }

    /// <summary>Record that a wave gave up waiting. See <see cref="TimedOut" />.</summary>
    internal void MarkTimedOut() => TimedOut = true;

    /// <summary>Clear the current scope, so a test starts from a flow that carries none.</summary>
    internal static void ResetSyncForTests() => _asyncCurrent.Value = null;

    private sealed class Restore : IDisposable
    {
        private readonly QuiescenceScope? _previous;

        internal Restore(QuiescenceScope? scope)
        {
            _previous = _asyncCurrent.Value;
            _asyncCurrent.Value = scope;
        }

        public void Dispose() => _asyncCurrent.Value = _previous;
    }
}
