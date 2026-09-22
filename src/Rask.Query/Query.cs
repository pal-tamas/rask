using Rask.Core.Live;
namespace Rask.Query;

/// <summary>
///     A live view of one cached query: what it holds now, whether it is fetching, and what went wrong.
/// </summary>
/// <remarks>
///     <para>
///         Hold one in a field and render it. Reading <see cref="Data" />, <see cref="IsLoading" /> or
///         <see cref="Error" /> during a render does two things: it starts a fetch if the entry is
///         missing or stale, and it registers the rendering component so the resolved result paints.
///     </para>
///     <para>
///         A query built from a lambda — <c>SessionQueryClient.Query(() =&gt; new GetOrders(Page))</c> — follows
///         its inputs by itself: every read runs the lambda, and a different answer re-points the query
///         at that entry. One built inside <c>Render</c> from the current value is re-pointed by the next
///         render's call. Either way there is nothing to call when a route parameter or a prop changes.
///     </para>
/// </remarks>
/// <typeparam name="TResult">What the query returns.</typeparam>
public sealed class Query<TResult> : IDisposable, IRenderSlotHandle
{
    private readonly SessionQueryClient _client;
    private readonly QuerySource<TResult>? _source;
    private readonly Action _onChanged;
    private readonly ComponentReaders _readers = new();
    private readonly CancellationTokenSource? _polling;

    // Held by a poll tick from its "still wanted?" check until its fetch has STARTED, and by Dispose while it
    // marks the query gone. Without it a tick could pass the check, lose the CPU, and start a fetch after
    // Dispose had returned — a polling query outliving its component by one request (#1126). The fetch starts
    // synchronously (QueryClient.RunAsync), so nothing is awaited while this is held.
    private readonly Lock _pollGate = new();
    private QueryEntry? _placeholder;
    private QueryEntry _entry;
    private QueryKey _key;
    private bool _paused;
    private bool _suspended;
    private bool _disposed;
    private long _lastReadGeneration;

    internal Query(SessionQueryClient client, QueryTarget target, QueryOptions options)
        : this(client, target, options, source: null)
    {
    }

    /// <summary>
    ///     A query whose target comes from <paramref name="source" /> — a lambda re-run on every read.
    /// </summary>
    /// <remarks>
    ///     Starts paused and runs the lambda at the first read rather than here: a query created in a
    ///     constructor would otherwise read route parameters and props before anything has bound them.
    /// </remarks>
    internal Query(SessionQueryClient client, QuerySource<TResult> source, QueryOptions options)
        : this(client, QueryTarget.Paused, options, source)
    {
    }

    private Query(
        SessionQueryClient client,
        QueryTarget target,
        QueryOptions options,
        QuerySource<TResult>? source)
    {
        _client = client;
        _source = source;
        _key = target.Key;
        _paused = target.IsPaused;
        Options = options;
        Fetch = target.Fetch;
        _onChanged = OnEntryChanged;
        _entry = client.Attach(target.Key, _onChanged, options.GcTime);

        // The one trigger TanStack calls "on mount": something has started observing this data, so
        // fetch it. Fire-and-forget because a constructor cannot await; the result arrives through
        // the entry and re-renders whoever read it.
        _ = client.EnsureFreshAsync(target.Key, this, CancellationToken.None);

        if (options.RefetchInterval is { } interval && interval > TimeSpan.Zero)
        {
            _polling = new CancellationTokenSource();
            _ = PollAsync(interval, _polling.Token);
        }
    }

    /// <summary>
    ///     Refetches on an interval for as long as this query is alive and something is watching it.
    /// </summary>
    /// <remarks>
    ///     A <c>Task.Delay</c> loop rather than a timer, which is what the rest of the repo does.
    ///     It stops on dispose, and also once every component that ever read this has been collected
    ///     — a query left undisposed must not keep a session fetching for ever. Disposing from
    ///     <c>OnUnmount</c> is still the mechanism; that second check is a safety net.
    /// </remarks>
    private async Task PollAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Task started;
            lock (_pollGate)
            {
                if (_disposed || (_readers.EverObserved && !_readers.HasLiveReaders))
                {
                    return;
                }

                // Set aside by a render that stopped using it: nothing is on screen to keep fresh until the
                // next read wakes it, and that read fetches if the entry went stale meanwhile.
                if (_suspended)
                {
                    continue;
                }

                _entry.Invalidate();
                started = _client.EnsureFreshAsync(_key, this, cancellationToken);
            }

            await started.ConfigureAwait(false);
        }
    }

    internal QueryOptions Options { get; }

    internal Func<CancellationToken, Task<object?>> Fetch { get; private set; }

    /// <summary>Whether a fetch may start: enabled by its options, and not waiting on an input.</summary>
    internal bool CanFetch => Options.Enabled && !_paused;

    /// <summary>
    ///     The result, or <c>default</c> until one has arrived — or the previous page's result while
    ///     a re-keyed query loads, when <see cref="QueryOptions.KeepPreviousData" /> is on.
    /// </summary>
    public TResult? Data
    {
        get
        {
            Touch();
            if (_entry.HasData)
            {
                return (TResult?)_entry.Data;
            }

            return Placeholder() is { } previous ? (TResult?)previous.Data : default;
        }
    }

    /// <summary>
    ///     What is on screen belongs to the previous key, and the current one is still loading.
    /// </summary>
    /// <remarks>
    ///     The cue to grey the rows rather than replace them. Without something saying so, keeping
    ///     the previous page shows stale data with no indication that it is stale.
    /// </remarks>
    public bool IsPlaceholderData
    {
        get
        {
            Touch();
            return !_entry.HasData && Placeholder() is not null;
        }
    }

    /// <summary>Whatever the last attempt threw, or null. Kept alongside stale data rather than replacing it.</summary>
    public Exception? Error
    {
        get
        {
            Touch();
            return _entry.Error;
        }
    }

    /// <summary>What this query holds: nothing yet, a failure, or a result.</summary>
    /// <remarks>
    ///     A query showing placeholder data reports <see cref="QueryStatus.Success" />: there is
    ///     something on screen, and reporting Pending would put a spinner over it, which is the whole
    ///     thing <see cref="QueryOptions.KeepPreviousData" /> exists to avoid.
    /// </remarks>
    public QueryStatus Status
    {
        get
        {
            Touch();
            return !_entry.HasData && Placeholder() is not null ? QueryStatus.Success : StatusOf(_entry);
        }
    }

    /// <summary>Whether a request is on the wire, and if not, whether one is being held back.</summary>
    public FetchStatus FetchStatus
    {
        get
        {
            Touch();
            return FetchStatusOf(_entry);
        }
    }

    /// <summary>
    ///     The first load, with nothing to show yet — the only state that warrants a spinner.
    /// </summary>
    /// <remarks>
    ///     False for a query held back by <see cref="QueryOptions.Enabled" />: it is pending, but
    ///     nothing is coming, and rendering a spinner for it would leave one turning for ever.
    /// </remarks>
    public bool IsLoading
    {
        get
        {
            Touch();
            return StatusOf(_entry) == QueryStatus.Pending && FetchStatusOf(_entry) == FetchStatus.Fetching;
        }
    }

    /// <summary>
    ///     A request is in flight. Unlike <see cref="IsLoading" /> this is also true while refreshing
    ///     data that is already on screen, which is the cue for a subtle indicator rather than a spinner.
    /// </summary>
    public bool IsFetching
    {
        get
        {
            Touch();
            return FetchStatusOf(_entry) == FetchStatus.Fetching;
        }
    }

    /// <summary>Data has arrived and the last attempt did not fail.</summary>
    public bool IsSuccess
    {
        get
        {
            Touch();
            return StatusOf(_entry) == QueryStatus.Success;
        }
    }

    /// <summary>The last attempt threw. Any earlier result is still on <see cref="Data" />.</summary>
    public bool IsError
    {
        get
        {
            Touch();
            return StatusOf(_entry) == QueryStatus.Error;
        }
    }

    /// <summary>
    ///     Points this query at <paramref name="target" /> — a new page, a new filter, a new route
    ///     parameter — and starts observing that entry instead. An unchanged target is a no-op.
    /// </summary>
    /// <param name="target">What to show now.</param>
    /// <param name="renderReaders">
    ///     False when called from a read: the component reading is rendering already and will show the
    ///     new entry, so asking it to render again would only buy a second, identical frame.
    /// </param>
    internal void Repoint(QueryTarget target, bool renderReaders)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (target.Key == _key && target.IsPaused == _paused)
        {
            return;
        }

        var previous = _entry;
        if (!_suspended)
        {
            _client.Detach(_key, _onChanged);
        }

        _suspended = false;
        _key = target.Key;
        _paused = target.IsPaused;
        Fetch = target.Fetch;
        _entry = _client.Attach(target.Key, _onChanged, Options.GcTime);

        // Captured only when the new key has nothing yet: navigating to a page already in cache
        // should show that page, not the one before it.
        _placeholder = Options.KeepPreviousData && !_entry.HasData && previous.HasData ? previous : null;
        _ = _client.EnsureFreshAsync(target.Key, this, CancellationToken.None);
        if (renderReaders)
        {
            _readers.RenderAll();
        }
    }

    /// <summary>
    ///     An edit to this query's cached result, for a command's <c>SendAsync</c> to show before the
    ///     server answers and undo if it refuses.
    /// </summary>
    /// <remarks>
    ///     Aimed at the entry this query shows now. Nothing cached means nothing is edited — a row the
    ///     server never confirmed is never invented — and a failure then makes the entry fetch.
    /// </remarks>
    /// <param name="update">Produces the optimistic result from the current one.</param>
    /// <returns>The edit, to pass to <c>SendAsync</c>.</returns>
    public OptimisticEdit Optimistic(Func<TResult, TResult> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new OptimisticEdit<TResult>(_client, Key, update);
    }

    /// <summary>Fetches again regardless of freshness, and paints the result.</summary>
    public Task RefetchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Resume();
        _entry.Invalidate();
        return _client.EnsureFreshAsync(_key, this, cancellationToken);
    }

    /// <summary>Stops observing the entry, which starts its GC clock once nothing else is watching.</summary>
    public void Dispose()
    {
        lock (_pollGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _polling?.Cancel();
        _polling?.Dispose();
        if (!_suspended)
        {
            _client.Detach(_key, _onChanged);
        }

        _readers.Clear();
    }

    /// <summary>The previous key's entry, while it is still worth showing.</summary>
    private QueryEntry? Placeholder() =>
        Options.KeepPreviousData && _placeholder is { HasData: true } previous ? previous : null;

    private static QueryStatus StatusOf(QueryEntry entry) => entry switch
    {
        { Error: not null } => QueryStatus.Error,
        { HasData: true } => QueryStatus.Success,
        _ => QueryStatus.Pending,
    };

    private FetchStatus FetchStatusOf(QueryEntry entry) => entry.InFlight is not null
        ? FetchStatus.Fetching
        : CanFetch ? FetchStatus.Idle : FetchStatus.Paused;

    /// <summary>
    ///     Registers the rendering component so a later result reaches it.
    /// </summary>
    /// <remarks>
    ///     Deliberately does <b>not</b> start a fetch. With the default <c>StaleTime</c> of zero an
    ///     entry is stale the moment it lands, so fetching from here would mean a request on every
    ///     property read — a request per render, for ever. TanStack's zero means "fetch when
    ///     something starts observing this", not "fetch continuously", and the triggers are the ones
    ///     elsewhere in this file: construction, re-keying, an explicit refetch, and invalidation.
    /// </remarks>
    private void Touch()
    {
        if (_disposed)
        {
            return;
        }

        Resume();

        // A lambda query re-runs its lambda here, at every read: that is what lets it follow a route
        // parameter, a prop or a field with nothing to call when one changes. The source compares the
        // new input with the last one before building anything, so an unchanged read allocates nothing.
        Advance();

        if (LiveRenderContext.RenderOwner(out var generation) is not null)
        {
            _lastReadGeneration = generation;
        }

        _readers.Observe();

        // A server render serves the HTML the browser will hold, so a query with nothing to show yet
        // has to be waited for or the page ships its spinner — the first paint, and the whole
        // document a crawler sees. The fetch is started inside the client and never returned to a
        // lifecycle hook, so the host cannot see it any other way; handing it over here, at the read,
        // waits for exactly the queries this page actually displays.
        //
        // Only when there is nothing to show. A query serving cached data while it revalidates has
        // real content to render and its refresh lands over the live connection — waiting for that
        // would make every cache hit pay full latency to change nothing. Retries need no special
        // case: the task completes when the policy gives up, and the host's own budget bounds it.
        if (StatusOf(_entry) == QueryStatus.Pending
            && FetchStatusOf(_entry) == FetchStatus.Fetching
            && _entry.InFlight is { } inFlight)
        {
            LiveRenderContext.AwaitBeforeFirstPaint(inFlight);
        }
    }

    /// <summary>
    ///     Re-renders whatever is observing, and refetches first if the entry has gone stale.
    /// </summary>
    /// <remarks>
    ///     This is what makes an invalidation visible: the cache marks the entry stale and notifies,
    ///     and the query that is on screen turns that into a request. It terminates because a
    ///     successful fetch notifies again with the entry no longer stale.
    /// </remarks>
    private void OnEntryChanged()
    {
        if (_entry.HasData)
        {
            // The real page landed; stop standing in for it.
            _placeholder = null;
        }

        if (!_disposed && _entry.NeedsRefetch)
        {
            _ = _client.EnsureFreshAsync(_key, this, CancellationToken.None);
        }

        _readers.RenderAll();
    }

    internal QueryEntry Entry => _entry;

    /// <summary>The key this query is currently watching.</summary>
    /// <remarks>
    ///     Public so a component can invalidate its own entry without restating how the key is built —
    ///     <c>client.Invalidate(query.Key, exact: true)</c>.
    /// </remarks>
    public QueryKey Key
    {
        get
        {
            Advance();
            return _key;
        }
    }

    /// <summary>
    ///     Stops watching the entry when the render that just returned neither asked for this query nor
    ///     read it. Reversible: the next read resumes it.
    /// </summary>
    /// <remarks>
    ///     Suspended rather than disposed, because a render slot cannot tell a query a render stopped
    ///     asking for from one a constructor or a <c>field ??=</c> made once and a component keeps — a
    ///     child built during its parent's render lands in the parent's slots, and is not rebuilt when the
    ///     parent renders again. Disposing would kill the child's query under it; suspending costs the
    ///     entry's observation and nothing else, so its GC clock starts and nothing leaks.
    /// </remarks>
    void IRenderSlotHandle.ReleaseUnlessReadIn(long generation)
    {
        if (_disposed || _suspended || _lastReadGeneration == generation)
        {
            return;
        }

        _suspended = true;
        _client.Detach(_key, _onChanged);
    }

    /// <summary>Whether this query has been set aside by a render that stopped using it.</summary>
    internal bool IsSuspended => _suspended;

    private void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        _entry = _client.Attach(_key, _onChanged, Options.GcTime);
        _ = _client.EnsureFreshAsync(_key, this, CancellationToken.None);
    }

    private void Advance()
    {
        if (!_disposed && _source is not null && _source.TryAdvance(_client, out var target))
        {
            Repoint(target, renderReaders: false);
        }
    }
}
