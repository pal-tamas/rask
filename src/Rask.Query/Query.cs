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
///         When the inputs change — a route parameter, a prop — call <see cref="SetMessage" /> from
///         <c>OnPropsChanged</c>. This is not optional bookkeeping: the message captured at
///         construction is the cache key, so without it a query built from <c>new GetOrders(Page: 1)</c>
///         keeps showing page one for ever, silently, however many times the page changes.
///     </para>
/// </remarks>
/// <typeparam name="TResult">What the query returns.</typeparam>
public sealed class Query<TResult> : IDisposable
{
    private readonly QueryClient _client;
    private readonly Action _onChanged;
    private readonly ComponentReaders _readers = new();
    private readonly CancellationTokenSource? _polling;
    private QueryEntry? _placeholder;
    private QueryEntry _entry;
    private QueryKey _key;
    private bool _disposed;

    internal Query(QueryClient client, QueryKey key, QueryOptions options, Func<CancellationToken, Task<object?>> fetch)
    {
        _client = client;
        _key = key;
        Options = options;
        Fetch = fetch;
        _onChanged = OnEntryChanged;
        _entry = client.Attach(key, _onChanged, options.GcTime);

        // The one trigger TanStack calls "on mount": something has started observing this data, so
        // fetch it. Fire-and-forget because a constructor cannot await; the result arrives through
        // the entry and re-renders whoever read it.
        _ = client.EnsureFreshAsync(key, this, CancellationToken.None);

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

            if (_disposed || (_readers.EverObserved && !_readers.HasLiveReaders))
            {
                return;
            }

            _entry.Invalidate();
            await _client.EnsureFreshAsync(_key, this, cancellationToken).ConfigureAwait(false);
        }
    }

    internal QueryOptions Options { get; }

    internal Func<CancellationToken, Task<object?>> Fetch { get; private set; }

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
    ///     Re-points this query at a different message — a new page, a new filter, a new route
    ///     parameter — and starts observing that entry instead.
    /// </summary>
    /// <remarks>
    ///     Call it from <c>OnPropsChanged</c>. An unchanged key is a no-op, so calling it
    ///     unconditionally is fine and is the safer habit.
    /// </remarks>
    /// <param name="message">The message whose result this query should now show.</param>
    public void SetMessage(Rask.Cqrs.IQuery<TResult> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = new QueryKey(message.GetType(), message, null);
        if (key == _key)
        {
            return;
        }

        var previous = _entry;
        _client.Detach(_key, _onChanged);
        _key = key;
        Fetch = _client.DispatchFetch(message);
        _entry = _client.Attach(key, _onChanged, Options.GcTime);

        // Captured only when the new key has nothing yet: navigating to a page already in cache
        // should show that page, not the one before it.
        _placeholder = Options.KeepPreviousData && !_entry.HasData && previous.HasData ? previous : null;
        _ = _client.EnsureFreshAsync(key, this, CancellationToken.None);
        _readers.RenderAll();
    }

    /// <summary>Fetches again regardless of freshness, and paints the result.</summary>
    public Task RefetchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _entry.Invalidate();
        return _client.EnsureFreshAsync(_key, this, cancellationToken);
    }

    /// <summary>Stops observing the entry, which starts its GC clock once nothing else is watching.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _polling?.Cancel();
        _polling?.Dispose();
        _client.Detach(_key, _onChanged);
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
        : Options.Enabled ? FetchStatus.Idle : FetchStatus.Paused;

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
        if (!_disposed)
        {
            _readers.Observe();
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

    internal QueryKey Key => _key;
}
