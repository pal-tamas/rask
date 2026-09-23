using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>The session's cache. One instance per live session, by DI scope.</summary>
internal sealed class SessionQueryClient : IQueryClient
{
    private readonly Dictionary<QueryKey, QueryEntry> _entries = [];
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    public SessionQueryClient(IDispatcher dispatcher, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
    }

    public Query<TResult> Query<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        QueryKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        // No key given means the message IS the key, compared structurally — which is what lets two
        // components asking the same thing share one entry and one request.
        return new Query<TResult>(
            this,
            new QueryTarget(key ?? MessageKey.For(message), DispatchFetch(message), IsPaused: false),
            options ?? QueryOptions.Default);
    }

    public Query<TResult> Query<TResult>(Func<IQuery<TResult>?> message, QueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new Query<TResult>(this, new MessageSource<TResult>(message), options ?? QueryOptions.Default);
    }

    public Query<TResult> Query<TInput, TResult>(
        QueryKey prefix,
        Func<TInput> input,
        Func<TInput, CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fetch);
        return new Query<TResult>(
            this,
            new InputSource<TInput, TResult>(prefix, input, fetch),
            options ?? QueryOptions.Default);
    }

    public Query<TResult> Query<TResult>(
        QueryKey key,
        Func<CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return new Query<TResult>(
            this,
            new QueryTarget(key, async ct => await fetch(ct).ConfigureAwait(false), IsPaused: false),
            options ?? QueryOptions.Default);
    }

    public async Task<TResult> FetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options ?? QueryOptions.Default;
        var key = MessageKey.For(message);
        var entry = GetOrAdd(key);
        entry.RequireGcTime(settings.GcTime);

        if (entry.HasData && !entry.IsStale(settings.StaleTime, _time.GetUtcNow()))
        {
            return (TResult)entry.Data!;
        }

        await RunAsync(entry, DispatchFetch(message), settings, cancellationToken)
            .ConfigureAwait(false);
        if (entry.Error is { } error)
        {
            throw error;
        }

        return (TResult)entry.Data!;
    }

    public async Task PrefetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await FetchAsync(message, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A prefetch is a guess about where the user is going. Throwing at the navigation that
            // triggered it would turn a speculative miss into a visible failure; the query that
            // really needs the data will fetch it again and report the failure then.
        }
    }

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        InvalidateDeclared(command);
    }

    public async Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await _dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        InvalidateDeclared(command);
        return result;
    }

    public Command<TCommand> Command<TCommand>()
        where TCommand : ICommand => new(this);

    public Command<TCommand, TResult> Command<TCommand, TResult>()
        where TCommand : ICommand<TResult> => new(this);

    public Command Command(params QueryKey[] invalidates)
    {
        ArgumentNullException.ThrowIfNull(invalidates);
        return new Command(this, [.. invalidates]);
    }

    public Subscription<TNotification> Subscribe<TNotification>()
        where TNotification : INotification =>
        new(this, NotificationTarget<TNotification>(null));

    public Subscription<TNotification> Subscribe<TNotification>(ISubscription<TNotification> subscription)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return new Subscription<TNotification>(this, NotificationTarget<TNotification>(subscription));
    }

    public Subscription<TNotification> Subscribe<TNotification>(Func<ISubscription<TNotification>?> subscription)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return new Subscription<TNotification>(this, new RecordSource<TNotification>(this, subscription));
    }

    public Subscription<T> Subscribe<TInput, T>(
        Func<TInput> input,
        Func<TInput, CancellationToken, IAsyncEnumerable<T>> stream)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(stream);
        return new Subscription<T>(this, new StreamSource<TInput, T>(input, stream));
    }

    /// <summary>
    ///     The subscription knobs the app configured in <c>Rask:Cqrs</c>, or their defaults where nothing did.
    /// </summary>
    internal CqrsExecutionOptions Subscriptions =>
        _dispatcher is Dispatcher dispatcher ? dispatcher.Subscriptions : CqrsExecutionOptions.Default;

    /// <summary>What a notification subscription watches: its type and its record, through the dispatcher.</summary>
    internal SubscriptionTarget<TNotification> NotificationTarget<TNotification>(object? subscription)
        where TNotification : INotification =>
        new(new NotificationIdentity(typeof(TNotification), subscription),
            (admitted, ct) => Watch<TNotification>(subscription, admitted, ct));

    /// <summary>What a function subscription watches: one input, handed to the stream it opens.</summary>
    internal static SubscriptionTarget<T>? StreamTarget<TInput, T>(
        TInput input,
        Func<TInput, CancellationToken, IAsyncEnumerable<T>> stream) =>
        input is null
            ? null
            : new SubscriptionTarget<T>(new StreamIdentity(input), (admitted, ct) => Admit(admitted, stream(input, ct), ct));

    // The dispatcher's own Watch knows when the subscription is admitted — after the policy, after the replay. Any
    // other IDispatcher (a test double) is taken as admitted the moment it is asked.
    private IAsyncEnumerable<TNotification> Watch<TNotification>(
        object? subscription,
        Action admitted,
        CancellationToken ct)
        where TNotification : INotification =>
        _dispatcher is Dispatcher dispatcher
            ? Typed<TNotification>(dispatcher.Watch(typeof(TNotification), subscription, admitted, ct), ct)
            : Admit(
                admitted,
                subscription is ISubscription<TNotification> record
                    ? _dispatcher.SubscribeAsync(record, ct)
                    : _dispatcher.SubscribeAsync<TNotification>(ct),
                ct);

    private static async IAsyncEnumerable<TNotification> Typed<TNotification>(
        IAsyncEnumerable<INotification> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var notification in source.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return (TNotification)notification;
        }
    }

    private static async IAsyncEnumerable<T> Admit<T>(
        Action admitted,
        IAsyncEnumerable<T> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        admitted();
        await foreach (var value in source.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return value;
        }
    }

    private sealed record NotificationIdentity(Type Type, object? Subscription);

    private sealed record StreamIdentity(object Input);

    private sealed class RecordSource<TNotification>(
        SessionQueryClient client,
        Func<ISubscription<TNotification>?> subscription) : SubscriptionSource<TNotification>
        where TNotification : INotification
    {
        private bool _asked;
        private ISubscription<TNotification>? _last;

        public override bool TryAdvance(out SubscriptionTarget<TNotification>? target)
        {
            var current = subscription();
            if (_asked && Equals(current, _last))
            {
                target = null;
                return false;
            }

            _asked = true;
            _last = current;
            target = current is null ? null : client.NotificationTarget<TNotification>(current);
            return true;
        }
    }

    private sealed class StreamSource<TInput, T>(
        Func<TInput> input,
        Func<TInput, CancellationToken, IAsyncEnumerable<T>> stream) : SubscriptionSource<T>
    {
        private bool _asked;
        private TInput _last = default!;

        public override bool TryAdvance(out SubscriptionTarget<T>? target)
        {
            var current = input();
            if (_asked && EqualityComparer<TInput>.Default.Equals(current, _last))
            {
                target = null;
                return false;
            }

            _asked = true;
            _last = current;
            target = StreamTarget(current, stream);
            return true;
        }
    }

    public void Invalidate<TQuery>() => Invalidate(typeof(TQuery));

    public void Invalidate(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);
        Invalidate(MessageKey.ForType(queryType));
    }

    public void Invalidate(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Invalidate(QueryKey.Of(key));
    }

    public void Invalidate(QueryKey key, bool exact = false)
    {
        // Prefix by default, exact on request — TanStack's rule, and the reason a key is ordered at all:
        // invalidating ["orders"] should reach every list and every detail beneath it.
        InvalidateWhere(candidate => exact ? candidate == key : candidate.Matches(key));
    }

    public void Invalidate(Func<QueryKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        InvalidateWhere(predicate);
    }

    public void InvalidateAll() => InvalidateWhere(_ => true);

    public void SetData<TResult>(IQuery<TResult> message, TResult data)
    {
        ArgumentNullException.ThrowIfNull(message);
        SetData(MessageKey.For(message), data);
    }

    public void SetData<TResult>(QueryKey key, TResult data) =>
        GetOrAdd(key).Succeeded(data, _time.GetUtcNow());

    /// <summary>Dispatches a void command, for a Command that owns the surrounding state.</summary>
    internal Task DispatchCommandAsync(ICommand command, CancellationToken cancellationToken) =>
        _dispatcher.SendAsync(command, cancellationToken);

    /// <summary>Dispatches a value-returning command, for a Command that owns the surrounding state.</summary>
    internal Task<TResult> DispatchCommandAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken) =>
        _dispatcher.SendAsync(command, cancellationToken);

    /// <summary>
    ///     The cached result for a message, when there is one. Used to snapshot before an optimistic
    ///     edit — the only honest undo is the previous value, since a caller's projection cannot be
    ///     inverted.
    /// </summary>
    internal bool TryGetData<TResult>(QueryKey key, out TResult? data)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry)
                && entry.HasData)
            {
                data = (TResult?)entry.Data;
                return true;
            }
        }

        data = default;
        return false;
    }

    /// <summary>Wraps a message as a fetch, so the entry stores the boxed result uniformly.</summary>
    internal Func<CancellationToken, Task<object?>> DispatchFetch<TResult>(IQuery<TResult> message) =>
        async ct => await _dispatcher.QueryAsync(message, ct).ConfigureAwait(false);

    internal QueryEntry Attach(QueryKey key, Action listener, TimeSpan gcTime)
    {
        var entry = GetOrAdd(key);
        lock (_gate)
        {
            entry.RequireGcTime(gcTime);
            entry.Observe(listener);
        }

        return entry;
    }

    internal void Detach(QueryKey key, Action listener)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.Unobserve(listener, _time.GetUtcNow());
            }
        }

        Collect();
    }

    /// <summary>
    ///     Starts a fetch when the entry is missing or stale and none is already running, and returns
    ///     whatever is in flight so a caller that can await does.
    /// </summary>
    internal Task EnsureFreshAsync<TResult>(
        QueryKey key,
        Query<TResult> query,
        CancellationToken cancellationToken)
    {
        if (!query.CanFetch)
        {
            return Task.CompletedTask;
        }

        var entry = GetOrAdd(key);
        lock (_gate)
        {
            if (entry.InFlight is { } running)
            {
                return running;
            }

            if (!entry.IsStale(query.Options.StaleTime, _time.GetUtcNow()))
            {
                return Task.CompletedTask;
            }
        }

        return RunAsync(entry, query.Fetch, query.Options, cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever a handler threw belongs on the query as Error, for the component to "
                        + "render. Letting it escape would fault a fire-and-forget task nobody awaits.")]
    private Task RunAsync(
        QueryEntry entry,
        Func<CancellationToken, Task<object?>> fetch,
        QueryOptions options,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            // Re-checked under the lock: two components rendering the same query in one frame both
            // reach here, and without this they would both start a request. This is the dedup.
            if (entry.InFlight is { } running)
            {
                return running;
            }

            // Registered BEFORE the work starts, and as a separate completion rather than the work's
            // own task. Starting the fetch first looks equivalent and is not: a fetch that completes
            // synchronously — an already-cached handler, a test double — runs Succeeded, which clears
            // InFlight, and only then does the registration set it to an already-finished task that
            // nothing will ever clear. The entry then looks permanently in flight and the query never
            // refetches again, for the rest of the session.
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Linked, so the fetch is cancelled either by the caller or by the entry losing its last
            // observer — a component unmounting should release the request it started.
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            entry.BeginFetch(completion.Task, cancellation);
        }

        // Started outside the lock: it runs synchronously up to its first await, and both Succeeded
        // and Failed notify observers — which re-renders components, and must not happen while this
        // holds the cache's lock.
        _ = Execute();
        return completion.Task;

        // A BOUNDED loop inside one attempt, deliberately, rather than letting a failure notify and
        // be re-entered as a fresh fetch. That shape is what produced an unbounded hot retry against
        // an already-unwell server the first time round; the entry's owed-fetch flag exists to stop
        // it, and retrying through the notification path would defeat it again.
        async Task Execute()
        {
            var worthRetrying = options.ShouldRetry ?? QueryOptions.IsWorthRetrying;
            var backoff = options.RetryDelay ?? QueryOptions.DefaultRetryDelay;

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var data = await fetch(cancellation.Token).ConfigureAwait(false);
                    entry.Succeeded(data, _time.GetUtcNow());
                    break;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Cancelled because nothing is rendering this any more, or because the caller
                    // asked. Neither is a failure to show: recording it would leave an error on an
                    // entry whose next observer would then render it, having done nothing wrong.
                    entry.Abandoned();
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt >= options.Retry || !worthRetrying(ex))
                    {
                        entry.Failed(ex);
                        break;
                    }

                    try
                    {
                        await Task.Delay(backoff(attempt), cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        entry.Abandoned();
                        break;
                    }
                }
            }

            completion.TrySetResult();
        }
    }

    private QueryEntry GetOrAdd(QueryKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new QueryEntry();
                _entries[key] = entry;
            }

            return entry;
        }
    }

    private void InvalidateWhere(Func<QueryKey, bool> predicate)
    {
        List<QueryEntry> affected;
        lock (_gate)
        {
            affected = [.. _entries.Where(pair => predicate(pair.Key)).Select(pair => pair.Value)];
        }

        foreach (var entry in affected)
        {
            entry.Invalidate();
        }

        // Notifying is what makes an invalidation visible: a rendered query re-reads, finds itself
        // stale, and starts the refetch. An entry nothing renders simply stays stale until something
        // does, which is the point of invalidating rather than evicting.
        foreach (var entry in affected)
        {
            entry.NotifyChanged();
        }
    }

    private void Collect()
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (var key in _entries
                         .Where(pair => pair.Value.IsCollectable(now))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _entries.Remove(key);
            }
        }
    }

    /// <summary>
    ///     Reads <see cref="InvalidatesAttribute" /> off the command.
    /// </summary>
    /// <remarks>
    ///     Attribute metadata on a type the app already references, not member reflection, so the
    ///     trimmer keeps it — the same reason attributes survive on a trimmed WASM publish.
    /// </remarks>
    internal void InvalidateDeclared(object command)
    {
        // GetCustomAttributes, plural: the attribute allows multiples so a command can name both the
        // message types it affects and a key prefix, and reading only the first would silently honour one
        // of them.
        foreach (var declaration in command.GetType().GetCustomAttributes<InvalidatesAttribute>())
        {
            foreach (var queryType in declaration.QueryTypes)
            {
                Invalidate(queryType);
            }

            if (declaration.KeyPrefix.Count > 0)
            {
                Invalidate(QueryKey.Of([.. declaration.KeyPrefix]));
            }
        }
    }
}
