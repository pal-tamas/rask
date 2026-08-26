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
///         Lookup mirrors <c>LiveRenderContext</c>: a <c>ThreadStatic</c> for the synchronous walk,
///         which is the hot path, falling back to an <c>AsyncLocal</c> for continuations that have
///         already hopped threads.
///     </para>
/// </remarks>
internal sealed class QuiescenceScope : IDisposable
{
    private static readonly AsyncLocal<QuiescenceScope?> _asyncCurrent = new();

    [ThreadStatic] private static QuiescenceScope? _syncCurrent;

    private readonly List<(Task Wrapped, Component Owner)> _pending = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>The scope collecting work for the render currently running, if any.</summary>
    internal static QuiescenceScope? Current => _syncCurrent ?? _asyncCurrent.Value;

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

        if (ReferenceEquals(_syncCurrent, this))
        {
            _syncCurrent = null;
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
        _syncCurrent = scope;
        _asyncCurrent.Value = scope;
        return scope;
    }

    /// <summary>
    ///     Re-establish <paramref name="captured" /> on the current thread, for code that has
    ///     crossed an <see cref="ExecutionContext.SuppressFlow" /> boundary and so lost the
    ///     <c>AsyncLocal</c>.
    /// </summary>
    internal static IDisposable Enter(QuiescenceScope? captured) => new Restore(captured);

    /// <summary>
    ///     Record a lifecycle hook's task, along with the component that owns it.
    /// </summary>
    /// <remarks>
    ///     Stores a wrapper that completes when <paramref name="task" /> does but never faults or
    ///     cancels, so a batch can be awaited with a plain <c>WhenAll</c>. Faults are already routed
    ///     to the nearest <c>ErrorBoundary</c> by the caller; re-observing them here would either
    ///     throw out of the wait or double-report.
    /// </remarks>
    internal void Track(Task task, Component owner)
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
                if (!owner.IsUnmountedInternal)
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

    /// <summary>
    ///     Clear the thread-static slot. xUnit reuses pool threads and a <c>ThreadStatic</c>
    ///     outlives an <c>await</c>, so a scope left behind by one test would be found by the next
    ///     — the same reason <c>LiveRenderContext</c> carries this hook.
    /// </summary>
    internal static void ResetSyncForTests()
    {
        _syncCurrent = null;
        _asyncCurrent.Value = null;
    }

    private sealed class Restore : IDisposable
    {
        private readonly QuiescenceScope? _previousAsync;
        private readonly QuiescenceScope? _previousSync;

        internal Restore(QuiescenceScope? scope)
        {
            _previousSync = _syncCurrent;
            _previousAsync = _asyncCurrent.Value;
            _syncCurrent = scope;
            _asyncCurrent.Value = scope;
        }

        public void Dispose()
        {
            _syncCurrent = _previousSync;
            _asyncCurrent.Value = _previousAsync;
        }
    }
}
