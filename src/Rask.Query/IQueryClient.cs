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

    /// <summary>Marks the named function-form entry stale.</summary>
    /// <param name="key">The key given when the query was created.</param>
    void Invalidate(string key);

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
}
