using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     The dispatcher, wrapped in a cache: dedup, staleness, background refetch and invalidation for
///     Rask components.
/// </summary>
/// <remarks>
///     <para>
///         Registered <b>scoped</b>, which on the Server host means one cache per live session — Rask
///         creates a service scope per session, so one visitor can never be served another's data.
///         That is not a setting to get right; it is the only arrangement this package offers,
///         because a process-wide cache in a multi-user host is a data leak with a plausible excuse.
///         On WASM and native the scope is the app, which is the same thing.
///     </para>
///     <para>Inject it, hold the returned <see cref="Rask.Query.Query{TResult}" /> in a field, and render it.</para>
/// </remarks>
public interface IQueryClient
{
    /// <summary>
    ///     A live view of a query's result. The message is the cache key, compared structurally — two
    ///     components asking for <c>new GetOrders(Page: 1)</c> share one entry and one request.
    /// </summary>
    /// <remarks>
    ///     Re-point it with <see cref="Rask.Query.Query{TResult}.SetMessage" /> from <c>OnPropsChanged</c> when
    ///     its inputs change, or it will keep showing the result it was created with.
    /// </remarks>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query to run and cache.</param>
    /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
    Query<TResult> Query<TResult>(IQuery<TResult> message, QueryOptions? options = null);

    /// <summary>
    ///     The same, under a key you choose — for putting a query into a hierarchy that spans message
    ///     types, so one <see cref="Invalidate(QueryKey, bool)" /> can reach all of them.
    /// </summary>
    /// <example>
    ///     <code>
    ///     client.Query(new GetOrders(page), QueryKey.Of("orders", "list", QueryKey.Fields(("page", page))));
    ///     client.Invalidate(QueryKey.Of("orders"));   // lists and details alike
    ///     </code>
    /// </example>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query to run and cache.</param>
    /// <param name="key">The key to cache it under, instead of the one the message derives.</param>
    /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
    Query<TResult> Query<TResult>(IQuery<TResult> message, QueryKey key, QueryOptions? options = null);

    /// <summary>
    ///     The same, for data that does not arrive through CQRS — a third-party HTTP call, a file read.
    /// </summary>
    /// <remarks>
    ///     <paramref name="key" /> is yours to keep unique, which is the one place in this package
    ///     where two different things can collide under one name. Prefer the message form wherever
    ///     there is a message, because there the key cannot drift from what it identifies.
    /// </remarks>
    /// <typeparam name="TResult">What the function returns.</typeparam>
    /// <param name="key">A name unique to this data within the session.</param>
    /// <param name="fetch">Runs when the entry is missing or stale.</param>
    /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
    Query<TResult> Query<TResult>(
        string key,
        Func<CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null);

    /// <summary>The same, under a multi-part key, so it can share a prefix with everything related to it.</summary>
    /// <typeparam name="TResult">What the function returns.</typeparam>
    /// <param name="key">The key to cache it under.</param>
    /// <param name="fetch">Runs when the entry is missing or stale.</param>
    /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
    Query<TResult> Query<TResult>(
        QueryKey key,
        Func<CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null);

    /// <summary>
    ///     Awaits a query's result, using and filling the cache, without creating a live view. For an
    ///     event handler that needs the current value rather than a component that renders it.
    /// </summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query to run.</param>
    /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    Task<TResult> FetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fills the cache ahead of time, so the component that wants this next paints immediately.
    /// </summary>
    /// <remarks>
    ///     Never throws. A prefetch is a guess about where the user is going, and a wrong guess must
    ///     not surface as a failure at the navigation that made it — the query that really needs the
    ///     data will fetch again and report the failure then.
    /// </remarks>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query to warm.</param>
    /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    Task PrefetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Dispatches a command and then invalidates whatever it declares with
    ///     <see cref="InvalidatesAttribute" />, so the affected queries refetch wherever they are on screen.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    Task MutateAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a command that returns a value, then invalidates what it declares.</summary>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    Task<TResult> MutateAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    ///     A command you can render — whether it is running, whether it failed, and what to disable
    ///     while it is in flight.
    /// </summary>
    /// <remarks>
    ///     Hold the result in a field. Unlike
    ///     <see cref="MutateAsync(ICommand, System.Threading.CancellationToken)" />, which is the
    ///     await-and-forget form, this one carries state a component can render.
    /// </remarks>
    /// <typeparam name="TCommand">The command to dispatch.</typeparam>
    Mutation<TCommand> Mutation<TCommand>()
        where TCommand : ICommand;

    /// <summary>A renderable command that returns a value.</summary>
    /// <typeparam name="TCommand">The command to dispatch.</typeparam>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    Mutation<TCommand, TResult> Mutation<TCommand, TResult>()
        where TCommand : ICommand<TResult>;

    /// <summary>
    ///     Marks every entry for a query message type stale. Anything rendering one refetches at once;
    ///     anything not rendered refetches when something next observes it.
    /// </summary>
    /// <remarks>
    ///     By type, not by exact message — invalidating <c>GetOrders</c> after a save should refresh
    ///     page one and page seven alike, not only whichever the caller happens to hold.
    /// </remarks>
    /// <typeparam name="TQuery">The query message type to invalidate.</typeparam>
    void Invalidate<TQuery>();

    /// <summary>Marks every entry for a query message type stale.</summary>
    /// <param name="queryType">The query message type to invalidate.</param>
    void Invalidate(Type queryType);

    /// <summary>Marks the named function-form entry, and anything beneath it, stale.</summary>
    /// <param name="key">The first part of the key given when the query was created.</param>
    void Invalidate(string key);

    /// <summary>
    ///     Marks every entry whose key <em>starts with</em> <paramref name="key" /> stale.
    /// </summary>
    /// <remarks>
    ///     Prefix matching is the point of an ordered key: <c>QueryKey.Of("orders")</c> reaches every list
    ///     and every detail beneath it, which a flat key cannot express. A
    ///     <see cref="QueryKey.Fields" /> part inside the filter is matched as a <em>subset</em>, so
    ///     <c>Fields(("status", "done"))</c> reaches every page of the done ones.
    /// </remarks>
    /// <param name="key">The prefix to match.</param>
    /// <param name="exact">Match the whole key instead, so only that one entry is affected.</param>
    void Invalidate(QueryKey key, bool exact = false);

    /// <summary>Marks every entry whose key satisfies <paramref name="predicate" /> stale.</summary>
    /// <remarks>
    ///     The escape hatch for what a prefix cannot say. Prefer a key that expresses the relationship —
    ///     a predicate is invisible to anyone reading the query's own declaration.
    /// </remarks>
    /// <param name="predicate">Decides, per cached key, whether that entry is now out of date.</param>
    void Invalidate(Func<QueryKey, bool> predicate);

    /// <summary>Marks every entry in this session's cache stale.</summary>
    void InvalidateAll();

    /// <summary>
    ///     Writes a result into the cache without fetching, so a command that already returned the
    ///     new state can paint it immediately instead of causing a round trip to re-read it.
    /// </summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query whose entry to write.</param>
    /// <param name="data">The result to store.</param>
    void SetData<TResult>(IQuery<TResult> message, TResult data);

    /// <summary>Writes a result into the entry under <paramref name="key" /> without fetching.</summary>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="key">The entry to write.</param>
    /// <param name="data">The result to store.</param>
    void SetData<TResult>(QueryKey key, TResult data);
}
