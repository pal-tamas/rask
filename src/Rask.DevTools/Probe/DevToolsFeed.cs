using System.Runtime.CompilerServices;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

/// <summary>Which way a frame crossed the wire, from the inspected page's point of view.</summary>
internal enum DevToolsWireDirection : byte
{
    /// <summary>The page sent it to the app: an event, a navigation, a hello.</summary>
    Out,

    /// <summary>The app sent it to the page: a render frame, an ack, a control frame.</summary>
    In,
}

/// <summary>One frame on the wire, as the panel's Wire tab lists it.</summary>
/// <param name="Sequence">Monotonic per feed, so the panel can key rows and tell what is new.</param>
/// <param name="Direction">Which way it went.</param>
/// <param name="Kind">The frame's <c>type</c> for inbound traffic; <c>frame</c> for a render the app sent.</param>
/// <param name="Bytes">Its UTF-8 size on the wire.</param>
/// <param name="Timestamp">A <see cref="System.Diagnostics.Stopwatch" /> timestamp.</param>
/// <param name="DiffOps">For a render frame, how many edit ops it carried; null for a full document or other traffic.</param>
internal readonly record struct DevToolsWireEvent(
    long Sequence,
    DevToolsWireDirection Direction,
    string Kind,
    int Bytes,
    long Timestamp,
    int? DiffOps);

/// <summary>
///     What the devtools have seen of one inspected session, bounded so a long-running page cannot grow it without limit.
/// </summary>
/// <remarks>
///     Written from the inspected session's render and dispatch paths and read from the panel's own session, so every
///     access takes the lock. Traffic is at most a few frames per interaction, which makes a lock cheaper than anything
///     cleverer and the ordering trivially right.
/// </remarks>
internal sealed class DevToolsFeed
{
    /// <summary>How many wire events a feed keeps; the oldest go first.</summary>
    internal const int WireCapacity = 1000;

    // How long a panel that opened between renders waits for a render in progress to let go of the page.
    private static readonly TimeSpan CaptureWait = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly DevToolsWireEvent[] _wire = new DevToolsWireEvent[WireCapacity];
    private int _wireStart;
    private int _wireCount;
    private long _sequence;

    // The op count the last diff produced, attached to the render frame that carries it: DiffComputed fires while the
    // payload is written, and FrameSent right after, on the same session.
    private int? _pendingDiffOps;

    // The last component tree, and how many panels are asking for one.
    private DevToolsComponentNode? _tree;
    private int _treeWatchers;

    // The last render's walk, the gate it was written under, and the snapshotter whose ids the panel keys on. Set by the
    // probe on the page's first render; a feed created before that has nothing to build from yet.
    private readonly DevToolsTreeCapture _capture = new();
    private volatile SemaphoreSlim? _captureGate;
    private volatile DevToolsTreeSnapshotter? _snapshots;

    // The render log: whole commits, bounded both by how many and by how many renders they hold, since one first render of
    // a big page can carry thousands.
    private readonly Queue<DevToolsCommit> _commits = new();
    private int _heldRenders;

    // Every component this feed has seen render, weakly: the first render of one is its mount.
    private static readonly object Seen = new();
    private readonly ConditionalWeakTable<Component, object> _rendered = new();

    /// <summary>How many commits a feed keeps; the oldest go first.</summary>
    internal const int CommitCapacity = 200;

    /// <summary>How many renders, across the commits held, a feed keeps before it drops the oldest commits.</summary>
    internal const int RenderCapacity = 5000;

    /// <summary>Raised after every recorded event, outside the lock. Subscribers must not block.</summary>
    internal event Action? Changed;

    internal void RecordDiff(int opCount, bool usedDiff)
    {
        lock (_gate)
        {
            _pendingDiffOps = usedDiff ? opCount : null;
        }
    }

    internal void RecordWire(DevToolsWireDirection direction, string kind, int bytes, long timestamp)
    {
        lock (_gate)
        {
            int? ops = null;
            if (direction == DevToolsWireDirection.In && kind == "frame")
            {
                ops = _pendingDiffOps;
                _pendingDiffOps = null;
            }

            var item = new DevToolsWireEvent(++_sequence, direction, kind, bytes, timestamp, ops);
            if (_wireCount < WireCapacity)
            {
                _wire[(_wireStart + _wireCount) % WireCapacity] = item;
                _wireCount++;
            }
            else
            {
                _wire[_wireStart] = item;
                _wireStart = (_wireStart + 1) % WireCapacity;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Whether a panel is showing this session's component tree, so the probe knows to build one.</summary>
    /// <remarks>
    ///     The walk is captured on every render regardless — into buffers that stop allocating once they fit the page — so
    ///     a panel that opens between renders has something to build from. Building the tree is the part that costs, and
    ///     the count is what keeps that with the panel that asked for it: no open panel, no tree.
    /// </remarks>
    internal bool WantsTree => Volatile.Read(ref _treeWatchers) > 0;

    /// <summary>
    ///     Asks for a tree, until the returned token is disposed. The first watcher gets one built from the page's last
    ///     render straight away, rather than waiting for the page to render again.
    /// </summary>
    internal IDisposable WatchTree()
    {
        // On the first watcher only, and whatever tree is already held: one kept from an earlier watch is stale.
        if (Interlocked.Increment(ref _treeWatchers) == 1)
        {
            _ = BuildFromCaptureAsync();
        }

        return new TreeWatch(this);
    }

    /// <summary>
    ///     Records the walk the page just finished. Called by the probe from inside that render, under its gate; builds the
    ///     tree there too when a panel is watching.
    /// </summary>
    internal void RecordWalk(
        Component root, List<DevToolsWalkItem> items, FrameWriter? frames, SemaphoreSlim? gate,
        DevToolsTreeSnapshotter snapshots)
    {
        _capture.Record(root, items, frames);
        _captureGate = gate;
        _snapshots = snapshots;

        if (WantsTree && snapshots.Snapshot(_capture) is { } tree)
        {
            RecordTree(tree);
        }
    }

    // A panel opened after the page's last render: build from that render's capture, under the same gate the render
    // wrote it under. A render that holds the gate past the wait is about to record a tree of its own anyway.
    private async Task BuildFromCaptureAsync()
    {
        if (_snapshots is not { } snapshots)
        {
            return;
        }

        var gate = _captureGate;
        try
        {
            if (gate is not null && !await gate.WaitAsync(CaptureWait).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            // The session ended between its last render and this panel opening; there is nothing left to show.
            return;
        }

        try
        {
            if (snapshots.Snapshot(_capture) is { } tree)
            {
                RecordTree(tree);
            }
        }
        finally
        {
            try
            {
                gate?.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while the tree was built: nothing is waiting on it any more.
            }
        }
    }

    internal void RecordTree(DevToolsComponentNode root)
    {
        lock (_gate)
        {
            _tree = root;
        }

        Changed?.Invoke();
    }

    /// <summary>The component tree as of the last render, or null while nothing has been recorded.</summary>
    internal DevToolsComponentNode? TreeSnapshot()
    {
        lock (_gate)
        {
            return _tree;
        }
    }

    /// <summary>One tab's interest in the tree, given up exactly once however often it is disposed.</summary>
    private sealed class TreeWatch(DevToolsFeed feed) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref feed._treeWatchers);
            }
        }
    }

    /// <summary>
    ///     Records the components that rendered in the commit the page just made. Called by the probe from inside that render;
    ///     a commit in which nothing rendered — every component served from its cache — is still a commit, and recorded.
    /// </summary>
    internal void RecordCommit(List<DevToolsRenderItem> renders, int walked, DevToolsTreeSnapshotter ids, long timestamp)
    {
        // Built outside the lock: the ids and names are the snapshotter's and the type cache's, and a mount is known by
        // the table below, which only this session's render thread writes.
        var items = new DevToolsRender[renders.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var (component, cause, self) = renders[i];
            var reason = _rendered.TryAdd(component, Seen) ? DevToolsRenderReason.Mount : DevToolsNames.ReasonOf(cause);
            items[i] = new DevToolsRender(
                ids.IdOf(component), DevToolsNames.Of(component.GetType()), component.Key?.ToString(), reason, self);
        }

        lock (_gate)
        {
            // Past either bound, the oldest commits go, but never the newest: a first render bigger than the whole budget
            // is still the commit a developer opened the tab to see.
            _commits.Enqueue(new DevToolsCommit(++_sequence, timestamp, walked, items));
            _heldRenders += items.Length;
            while (_commits.Count > 1 && (_commits.Count > CommitCapacity || _heldRenders > RenderCapacity))
            {
                _heldRenders -= _commits.Dequeue().Renders.Length;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The commits currently held, oldest first.</summary>
    internal DevToolsCommit[] CommitsSnapshot()
    {
        lock (_gate)
        {
            return _commits.ToArray();
        }
    }

    /// <summary>Forgets every commit held, so the Renders tab counts from here. Mounts already seen stay seen.</summary>
    internal void ClearCommits()
    {
        lock (_gate)
        {
            _commits.Clear();
            _heldRenders = 0;
        }

        Changed?.Invoke();
    }

    /// <summary>The wire events currently held, oldest first.</summary>
    internal DevToolsWireEvent[] WireSnapshot()
    {
        lock (_gate)
        {
            var copy = new DevToolsWireEvent[_wireCount];
            for (var i = 0; i < _wireCount; i++)
            {
                copy[i] = _wire[(_wireStart + i) % WireCapacity];
            }

            return copy;
        }
    }
}
