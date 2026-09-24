using Rask.Core;
using Rask.Core.Live;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     A live view of a stream of values — every notification published for what it watches, or whatever a function
///     stream yields: the latest one, whether it is connected, and what went wrong.
/// </summary>
/// <remarks>
///     <para>
///         Declared where a query is — in <c>Render</c>, a <c>field ??=</c> property or the constructor — and read the
///         same way. Reading <see cref="Data" />, <see cref="Status" /> or <see cref="Error" /> during a render registers
///         the component, and every value that arrives re-renders it. Nothing to dispose: it closes with the component
///         that first read it, and a render that stops asking for it sets it aside until it is read again.
///     </para>
///     <code>
///     var shipped = QueryClient.Subscribe&lt;OrderShipped&gt;(Id);
///
///     return shipped.IsLoading ? Spinner() : Badge[shipped.Data?.Status ?? order.Data?.Status];
///     </code>
///     <para>
///         It starts with the last value published for what it watches, when there is one, so a page opened after the
///         fact still shows it. A dropped connection is reopened by itself — <see cref="IsLive" /> is false meanwhile,
///         and <see cref="Data" /> keeps the last value — and every query it patches with <see cref="Into{TResult}" /> is
///         refetched once it is back, so whatever was missed while it was away is not lost.
///     </para>
/// </remarks>
/// <typeparam name="T">What the stream carries.</typeparam>
public sealed class Subscription<T> : IDisposable, IRenderSlotHandle
{
    private readonly SessionQueryClient _client;
    private readonly SubscriptionSource<T>? _source;
    private readonly ComponentReaders _readers = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<object, Patch> _into = new(ReferenceEqualityComparer.Instance);

    private SubscriptionTarget<T>? _target;
    private CancellationTokenSource? _run;
    private TaskCompletionSource _admitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private T? _data;
    private bool _hasData;
    private T[] _items = [];
    private int _keep;
    private SubscriptionStatus _status = SubscriptionStatus.Connecting;
    private Exception? _error;
    private bool _suspended;
    private bool _disposed;
    private bool _ownerBound;
    private long _lastReadGeneration;

    internal Subscription(SessionQueryClient client, SubscriptionTarget<T> target)
    {
        _client = client;
        _target = target;
        Start();
    }

    /// <summary>A subscription whose target comes from a lambda re-run at every read; it opens at the first one.</summary>
    internal Subscription(SessionQueryClient client, SubscriptionSource<T> source)
    {
        _client = client;
        _source = source;
    }

    /// <summary>The latest value, or <c>default</c> until one has arrived. Kept while reconnecting.</summary>
    public T? Data
    {
        get
        {
            Touch();
            lock (_gate)
            {
                return _data;
            }
        }
    }

    /// <summary>
    ///     The last values, oldest first, when <see cref="Keep" /> asked for them; empty otherwise. A chat, a log, the
    ///     orders placed since the page opened.
    /// </summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            Touch();
            return Volatile.Read(ref _items);
        }
    }

    /// <summary>Where it is: connecting, live, reconnecting, ended or refused.</summary>
    public SubscriptionStatus Status
    {
        get
        {
            Touch();
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Why it is not live: the refusal, or what dropped the connection it is reopening. Null while live.</summary>
    public Exception? Error
    {
        get
        {
            Touch();
            lock (_gate)
            {
                return _error;
            }
        }
    }

    /// <summary>Opening and not yet admitted, with nothing to show — the only state that warrants a spinner.</summary>
    /// <remarks>False while paused on a lambda that returned null: nothing is coming, so a spinner would turn for ever.</remarks>
    public bool IsLoading
    {
        get
        {
            Touch();
            lock (_gate)
            {
                return _status == SubscriptionStatus.Connecting && !_hasData && _target is not null;
            }
        }
    }

    /// <summary>Open: new values arrive as they happen.</summary>
    public bool IsLive => Status == SubscriptionStatus.Live;

    /// <summary>The connection dropped and is being reopened; <see cref="Data" /> keeps the last value.</summary>
    public bool IsReconnecting => Status == SubscriptionStatus.Reconnecting;

    /// <summary>A function stream ran to its end.</summary>
    public bool IsEnded => Status == SubscriptionStatus.Ended;

    /// <summary>Refused, or broken for good; see <see cref="Error" />.</summary>
    public bool IsError => Status == SubscriptionStatus.Error;

    /// <summary>
    ///     Keeps the last <paramref name="count" /> values in <see cref="Items" />, oldest first. Without it only the
    ///     latest is held, in <see cref="Data" />.
    /// </summary>
    /// <param name="count">How many to keep; at least one.</param>
    /// <returns>This subscription, so it reads as one line: <c>QueryClient.Subscribe&lt;ChatMessage&gt;(Room).Keep(100)</c>.</returns>
    public Subscription<T> Keep(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        lock (_gate)
        {
            _keep = count;
            if (_items.Length > count)
            {
                Volatile.Write(ref _items, _items[^count..]);
            }
        }

        return this;
    }

    /// <summary>
    ///     Patches <paramref name="query" />'s cached result with each value that arrives, so the list on screen changes
    ///     in place with no round trip.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Aimed at whatever the query shows now, like an optimistic edit, and skipped while it holds nothing — a row
    ///         the server never sent is never invented. After a dropped connection is back, the query is refetched once,
    ///         so a value missed meanwhile is not lost.
    ///     </para>
    ///     <para>
    ///         Write the patch so applying it twice changes nothing — replace by id rather than prepend — because the
    ///         query's own refetch may already include the value.
    ///     </para>
    ///     <code>
    ///     var orders = QueryClient.Query(new GetOrders(Page));
    ///     QueryClient.Subscribe&lt;OrderShipped&gt;()
    ///         .Into(orders, (list, e) =&gt; [.. list.Select(o =&gt; o.Id == e.OrderId ? o with { Status = e.Status } : o)]);
    ///     </code>
    /// </remarks>
    /// <typeparam name="TResult">What the query holds.</typeparam>
    /// <param name="query">The query to patch.</param>
    /// <param name="patch">Produces the query's new result from its current one and the value that arrived.</param>
    /// <returns>This subscription.</returns>
    public Subscription<T> Into<TResult>(Query<TResult> query, Func<TResult, T, TResult> patch)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(patch);

        // Keyed by the query, so the call a render repeats every time replaces its patch rather than stacking another.
        var edit = new Patch(
            value =>
            {
                var key = query.Key;
                if (_client.TryGetData<TResult>(key, out var current))
                {
                    _client.Set(key, patch(current!, (T)value!));
                }
            },
            () => _client.Invalidate(query.Key.Only()));

        lock (_gate)
        {
            _into[query] = edit;
        }

        return this;
    }

    /// <summary>Closes the subscription. Not needed for one a component declared: it closes with the component.</summary>
    public void Dispose()
    {
        CancellationTokenSource? run;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            run = _run;
            _run = null;
        }

        Cancel(run);
        _admitted.TrySetResult();
        _readers.Clear();
    }

    /// <summary>
    ///     Points this subscription at <paramref name="target" /> — a different key, a different input — dropping what
    ///     the old one held. An unchanged target is a no-op.
    /// </summary>
    internal void Repoint(SubscriptionTarget<T>? target)
    {
        if (_disposed || Equals(target?.Identity, _target?.Identity))
        {
            return;
        }

        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _run;
            _run = null;
            _admitted.TrySetResult();
            _target = target;
            _data = default;
            _hasData = false;
            Volatile.Write(ref _items, []);
            _error = null;
            _status = SubscriptionStatus.Connecting;
        }

        Cancel(previous);
        Start();
    }

    /// <summary>Whether this subscription has been set aside by a render that stopped using it.</summary>
    internal bool IsSuspended => _suspended;

    void IRenderSlotHandle.ReleaseUnlessReadIn(long generation)
    {
        CancellationTokenSource? run;
        lock (_gate)
        {
            if (_disposed || _suspended || _lastReadGeneration == generation)
            {
                return;
            }

            _suspended = true;
            run = _run;
            _run = null;
            _admitted.TrySetResult();
        }

        Cancel(run);
    }

    private void Start()
    {
        SubscriptionTarget<T> target;
        CancellationTokenSource run;
        lock (_gate)
        {
            if (_target is null || _disposed || _suspended || _run is not null)
            {
                return;
            }

            target = _target;
            _run = run = new CancellationTokenSource();
            if (_admitted.Task.IsCompleted)
            {
                _admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        // Runs synchronously up to its first real wait, so a replayed value is already in Data when a constructor or a
        // render that created this returns.
        _ = RunAsync(target, run);
    }

    private async Task RunAsync(SubscriptionTarget<T> target, CancellationTokenSource run)
    {
        var token = run.Token;
        var attempt = 0;
        var opened = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await foreach (var value in target.Open(Admitted, token).WithCancellation(token).ConfigureAwait(false))
                {
                    Received(value, run);
                }

                Settle(SubscriptionStatus.Ended, error: null, run);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsFinal(ex))
            {
                Settle(SubscriptionStatus.Error, ex, run);
                return;
            }
#pragma warning disable CA1031 // Anything else dropped the connection: it belongs on Error while this reopens it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Settle(SubscriptionStatus.Reconnecting, ex, run);
            }

            try
            {
                await Task.Delay(Backoff(attempt++, _client.Subscriptions), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        void Admitted()
        {
            Patch[] reopened = [];
            lock (_gate)
            {
                if (!IsCurrent(run))
                {
                    return;
                }

                _status = SubscriptionStatus.Live;
                _error = null;
                if (opened)
                {
                    reopened = [.. _into.Values];
                }
            }

            // Reopened after a drop: whatever was published meanwhile never arrived, so what it patches is fetched again.
            foreach (var patch in reopened)
            {
                patch.Refetch();
            }

            opened = true;
            attempt = 0;
            _admitted.TrySetResult();
            _readers.RenderAll();
        }
    }

    private void Received(T value, CancellationTokenSource run)
    {
        Patch[] patches;
        lock (_gate)
        {
            // The run that produced this must still be THIS subscription's run: a value a cancelled run had already
            // handed over must not land in what a re-point has since made this — the next order's page showing the
            // previous order's status.
            if (!IsCurrent(run))
            {
                return;
            }

            _data = value;
            _hasData = true;
            if (_keep > 0)
            {
                var items = _items;
                Volatile.Write(ref _items, items.Length < _keep ? [.. items, value] : [.. items[1..], value]);
            }

            patches = _into.Count == 0 ? [] : [.. _into.Values];
        }

        foreach (var patch in patches)
        {
            patch.Apply(value);
        }

        // Every component that ever read this has gone without closing it — a subscription held in a longer-lived field:
        // nothing is left to show a value to, so stop listening rather than hold a feed open for nobody.
        if (_readers.EverObserved && !_readers.HasLiveReaders)
        {
            Dispose();
            return;
        }

        _readers.RenderAll();
    }

    private void Settle(SubscriptionStatus status, Exception? error, CancellationTokenSource run)
    {
        lock (_gate)
        {
            if (!IsCurrent(run))
            {
                return;
            }

            _status = status;
            _error = error;
        }

        // A first paint waiting on this must not wait on a subscription that is not coming.
        _admitted.TrySetResult();
        _readers.RenderAll();
    }

    private void Touch()
    {
        if (_disposed)
        {
            return;
        }

        Resume();

        // A lambda subscription re-runs its lambda here, at every read — what lets it follow a route parameter or a prop.
        if (_source is not null && _source.TryAdvance(out var target))
        {
            Repoint(target);
        }

        if (LiveRenderContext.RenderOwner(out var generation) is { } owner)
        {
            _lastReadGeneration = generation;
            BindTo(owner);
        }

        _readers.Observe();

        // A server render serves the HTML the browser will hold, so a subscription with nothing yet has to be waited for
        // until it is admitted — by then any replayed value is in Data — or the page ships its spinner.
        if (_status == SubscriptionStatus.Connecting && !_hasData && _target is not null)
        {
            LiveRenderContext.AwaitBeforeFirstPaint(_admitted.Task);
        }
    }

    // Closes with the component that first read it — its lifetime token, cancelled once at unmount — so a subscription
    // never outlives the page it was showing.
    private void BindTo(Component owner)
    {
        if (_ownerBound)
        {
            return;
        }

        _ownerBound = true;
        owner.LifetimeTokenInternal.Register(static state => ((Subscription<T>)state!).Dispose(), this);
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
        }

        Start();
    }

    /// <summary>Whether <paramref name="run" /> is still the run this subscription is showing. Caller holds the gate.</summary>
    private bool IsCurrent(CancellationTokenSource run) => ReferenceEquals(_run, run) && !run.IsCancellationRequested;

    private static void Cancel(CancellationTokenSource? run)
    {
        if (run is null)
        {
            return;
        }

        run.Cancel();
        run.Dispose();
    }

    // Final is what reopening can never fix: a refusal, a key the server will not take, a missing registration.
    private static bool IsFinal(Exception exception) => exception switch
    {
        // A disposed HttpClient or response stream — a host restarting, a container rebuilt — is a connection that
        // dropped, and reopening is exactly right. It derives from InvalidOperationException, so it comes first.
        ObjectDisposedException => false,
        UnauthorizedAccessException or ArgumentException or InvalidOperationException => true,
        RemoteDispatchException { StatusCode: >= 400 and < 500 and not 408 and not 429 } => true,
        _ => false,
    };

    // Rask:Cqrs:SubscriptionReconnectDelay, doubling up to Rask:Cqrs:SubscriptionReconnectCeiling.
    private static TimeSpan Backoff(int attempt, CqrsExecutionOptions options)
    {
        var delay = options.SubscriptionReconnectDelay * Math.Pow(2, Math.Min(attempt, 6));
        return delay < options.SubscriptionReconnectCeiling ? delay : options.SubscriptionReconnectCeiling;
    }

    private sealed record Patch(Action<object?> Apply, Action Refetch);
}

/// <summary>What a subscription watches, and how to open it.</summary>
/// <param name="Identity">Compared to decide whether a new call watches something else: the type and key, or the input.</param>
/// <param name="Open">Opens the stream, calling its argument once the stream is admitted.</param>
internal sealed record SubscriptionTarget<T>(object Identity, Func<Action, CancellationToken, IAsyncEnumerable<T>> Open);

/// <summary>A lambda re-run at every read, answering what the subscription should watch now.</summary>
internal abstract class SubscriptionSource<T>
{
    /// <summary>True when the answer changed since the last read; <paramref name="target" /> null means "wait".</summary>
    public abstract bool TryAdvance(out SubscriptionTarget<T>? target);
}
