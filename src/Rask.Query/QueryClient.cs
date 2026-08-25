using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>The session's cache. One instance per live session, by DI scope.</summary>
internal sealed class QueryClient : IQueryClient
{
    private readonly Dictionary<QueryKey, QueryEntry> _entries = [];
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    public QueryClient(IDispatcher dispatcher, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
    }

    public Query<TResult> Query<TResult>(IQuery<TResult> message, QueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new Query<TResult>(
            this,
            new QueryKey(message.GetType(), message, null),
            options ?? QueryOptions.Default,
            DispatchFetch(message));
    }

    public Query<TResult> Query<TResult>(
        string key,
        Func<CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(fetch);
        return new Query<TResult>(
            this,
            new QueryKey(typeof(TResult), null, key),
            options ?? QueryOptions.Default,
            async ct => await fetch(ct).ConfigureAwait(false));
    }

    public async Task<TResult> FetchAsync<TResult>(
        IQuery<TResult> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var key = new QueryKey(message.GetType(), message, null);
        var entry = GetOrAdd(key);

        if (entry.HasData && !entry.IsStale(QueryOptions.Default.StaleTime, _time.GetUtcNow()))
        {
            return (TResult)entry.Data!;
        }

        await RunAsync(entry, DispatchFetch(message), cancellationToken).ConfigureAwait(false);
        if (entry.Error is { } error)
        {
            throw error;
        }

        return (TResult)entry.Data!;
    }

    public async Task MutateAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        InvalidateDeclared(command);
    }

    public async Task<TResult> MutateAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        InvalidateDeclared(command);
        return result;
    }

    public void Invalidate<TQuery>() => Invalidate(typeof(TQuery));

    public void Invalidate(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);
        InvalidateWhere(key => key.Group == queryType);
    }

    public void Invalidate(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        InvalidateWhere(candidate => candidate.Name == key);
    }

    public void InvalidateAll() => InvalidateWhere(_ => true);

    public void SetData<TResult>(IQuery<TResult> message, TResult data)
    {
        ArgumentNullException.ThrowIfNull(message);
        GetOrAdd(new QueryKey(message.GetType(), message, null)).Succeeded(data, _time.GetUtcNow());
    }

    /// <summary>Wraps a message as a fetch, so the entry stores the boxed result uniformly.</summary>
    internal Func<CancellationToken, Task<object?>> DispatchFetch<TResult>(IQuery<TResult> message) =>
        async ct => await _dispatcher.DispatchAsync(message, ct).ConfigureAwait(false);

    internal QueryEntry Attach(QueryKey key, Action listener)
    {
        var entry = GetOrAdd(key);
        lock (_gate)
        {
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
        if (!query.Options.Enabled)
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

        return RunAsync(entry, query.Fetch, cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever a handler threw belongs on the query as Error, for the component to "
                        + "render. Letting it escape would fault a fire-and-forget task nobody awaits.")]
    private Task RunAsync(
        QueryEntry entry,
        Func<CancellationToken, Task<object?>> fetch,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
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
            entry.BeginFetch(completion.Task);
        }

        // Started outside the lock: it runs synchronously up to its first await, and both Succeeded
        // and Failed notify observers — which re-renders components, and must not happen while this
        // holds the cache's lock.
        _ = Execute();
        return completion.Task;

        async Task Execute()
        {
            try
            {
                var data = await fetch(cancellationToken).ConfigureAwait(false);
                entry.Succeeded(data, _time.GetUtcNow());
            }
            catch (Exception ex)
            {
                entry.Failed(ex);
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
                         .Where(pair => pair.Value.IsCollectable(QueryOptions.Default.GcTime, now))
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
    private void InvalidateDeclared(object command)
    {
        if (command.GetType().GetCustomAttribute<InvalidatesAttribute>() is not { } declaration)
        {
            return;
        }

        foreach (var queryType in declaration.QueryTypes)
        {
            Invalidate(queryType);
        }
    }
}
