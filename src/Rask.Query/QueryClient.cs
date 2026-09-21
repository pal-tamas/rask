using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     The session's query cache, from anywhere a component runs — its constructor, a <c>field ??=</c>
///     property, <c>Render</c> or an event handler — with nothing to inject.
/// </summary>
/// <remarks>
///     <para>
///         Every call reaches the cache of the session it runs in: the handler being dispatched, else the
///         render in progress. One per live session on the Server host and one per app in the browser, as
///         with the injected <see cref="IQueryClient" /> — never a process-wide cache, which in a
///         multi-user host would serve one visitor another's data. Outside a session (a hosted service, a
///         timer started at boot) it throws; inject <see cref="IQueryClient" /> there instead.
///     </para>
///     <para>Three places to declare a query, all following their inputs with nothing to call when one changes:</para>
///     <code>
///     // in Render, from the current values — the same call gets the same handle every render
///     var person = QueryClient.Query(new GetPerson(Id));
///
///     // in a field or the constructor, from a lambda — re-run at every read
///     Query&lt;PersonDetail&gt; Person =&gt; field ??= QueryClient.Query(() =&gt; new GetPerson(Id));
///     </code>
/// </remarks>
public static class QueryClient
{
    // ---- queries in Render: from the current values ---------------------------------------------------

    /// <summary>
    ///     The query for <paramref name="message" />. Inside <c>Render</c>, the same call returns the same
    ///     query every render, re-pointed at whatever message this render passes.
    /// </summary>
    /// <remarks>
    ///     Outside a render — a constructor run by the router, an event handler — it is a new query, as
    ///     <see cref="IQueryClient.Query{TResult}(IQuery{TResult}, QueryOptions?, QueryKey?)" /> makes one.
    ///     <paramref name="options" /> are read when the query is created; a later render's are ignored.
    /// </remarks>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <param name="message">The query message, which is also its cache key unless <paramref name="key" /> is given.</param>
    /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
    /// <param name="key">
    ///     The key to cache it under instead of the one the message derives, to put it in a hierarchy that
    ///     spans message types.
    /// </param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Query<TResult> Query<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        QueryKey? key = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(message);
        var client = Current();
        if (RenderSlots.For(callerFile, callerLine, key: null) is not { } slot)
        {
            return client.Query(message, options, key);
        }

        if (slot.Handle is Query<TResult> existing)
        {
            if (slot.Last is not Asked<TResult> asked || !Equals(asked.Message, message) || asked.Key != key)
            {
                existing.Repoint(
                    new QueryTarget(key ?? MessageKey.For(message), client.DispatchFetch(message), IsPaused: false),
                    renderReaders: false);
                slot.Last = new Asked<TResult>(message, key);
            }

            return existing;
        }

        var created = client.Query(message, options, key);
        slot.Handle = created;
        slot.Last = new Asked<TResult>(message, key);
        return created;
    }

    /// <summary>
    ///     The function query for <paramref name="input" /> under <paramref name="prefix" />: its key is
    ///     <c>[..prefix, input]</c>, and <paramref name="fetch" /> is handed the same input. Inside
    ///     <c>Render</c> the same call returns the same query every render.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     var hits = QueryClient.Query(QueryKey.For&lt;Person&gt;(), _search,
    ///         (s, ct) =&gt; Person.Read.Where(p =&gt; p.Name.Contains(s)).ToListAsync(ct));
    ///     </code>
    ///     A null input pauses the query until there is one.
    /// </remarks>
    /// <typeparam name="TInput">What the key and the fetch are built from.</typeparam>
    /// <typeparam name="TResult">What the fetch returns.</typeparam>
    /// <param name="prefix">What the data is about — <c>QueryKey.For&lt;Person&gt;()</c>, or a name.</param>
    /// <param name="input">This render's input.</param>
    /// <param name="fetch">Loads the data for one input.</param>
    /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Query<TResult> Query<TInput, TResult>(
        QueryKey prefix,
        TInput input,
        Func<TInput, CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        var client = Current();
        var target = RenderSlots.For(callerFile, callerLine, key: null);

        if (target?.Handle is Query<TResult> existing && target.Last is Seen<TInput> seen)
        {
            if (seen.Prefix != prefix || !EqualityComparer<TInput>.Default.Equals(seen.Input, input))
            {
                existing.Repoint(QueryTarget.ForInput(prefix, input, fetch), renderReaders: false);
                seen.Prefix = prefix;
                seen.Input = input;
            }

            return existing;
        }

        var created = new Query<TResult>(client, QueryTarget.ForInput(prefix, input, fetch), options ?? QueryOptions.Default);
        if (target is not null)
        {
            target.Handle = created;
            target.Last = new Seen<TInput> { Prefix = prefix, Input = input };
        }

        return created;
    }

    /// <summary>
    ///     The function query cached under <paramref name="key" />. Inside <c>Render</c> the same call
    ///     returns the same query every render, re-pointed when the key changes.
    /// </summary>
    /// <remarks>
    ///     Prefer <see cref="Query{TInput, TResult}(QueryKey, TInput, Func{TInput, CancellationToken, Task{TResult}}, QueryOptions?, string, int)" />
    ///     when the fetch reads a value: there the fetch is handed the value the key was built from.
    /// </remarks>
    /// <typeparam name="TResult">What the function returns.</typeparam>
    /// <param name="key">A name unique to this data within the session; a string converts to one.</param>
    /// <param name="fetch">Runs when the entry is missing or stale.</param>
    /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Query<TResult> Query<TResult>(
        QueryKey key,
        Func<CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        var client = Current();
        if (RenderSlots.For(callerFile, callerLine, key: null) is not { } slot)
        {
            return client.Query(key, fetch, options);
        }

        if (slot.Handle is Query<TResult> existing)
        {
            if (slot.Last is not QueryKey last || last != key)
            {
                existing.Repoint(
                    new QueryTarget(key, async ct => await fetch(ct).ConfigureAwait(false), IsPaused: false),
                    renderReaders: false);
                slot.Last = key;
            }

            return existing;
        }

        var created = client.Query(key, fetch, options);
        slot.Handle = created;
        slot.Last = key;
        return created;
    }

    // ---- queries in a field or the constructor: from a lambda ------------------------------------------

    /// <inheritdoc cref="IQueryClient.Query{TResult}(Func{IQuery{TResult}}, QueryOptions?)" />
    public static Query<TResult> Query<TResult>(Func<IQuery<TResult>?> message, QueryOptions? options = null) =>
        Current().Query(message, options);

    /// <inheritdoc cref="IQueryClient.Query{TInput, TResult}(QueryKey, Func{TInput}, Func{TInput, CancellationToken, Task{TResult}}, QueryOptions?)" />
    public static Query<TResult> Query<TInput, TResult>(
        QueryKey prefix,
        Func<TInput> input,
        Func<TInput, CancellationToken, Task<TResult>> fetch,
        QueryOptions? options = null) =>
        Current().Query(prefix, input, fetch, options);

    // ---- commands -----------------------------------------------------------------------------------

    /// <summary>
    ///     A renderable command for <typeparamref name="TCommand" />. Inside <c>Render</c> the same call
    ///     returns the same command every render, so its pending state survives the re-render it causes.
    /// </summary>
    /// <typeparam name="TCommand">The command to dispatch.</typeparam>
    /// <param name="key">
    ///     Identity for a command rendered in a loop — a Delete button per row — so each row keeps its own
    ///     pending state. Without it every call site is one command however many times it runs.
    /// </param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Command<TCommand> Command<TCommand>(
        object? key = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
        where TCommand : ICommand =>
        Slotted(callerFile, callerLine, key, static client => client.Command<TCommand>());

    /// <inheritdoc cref="Command{TCommand}(object?, string, int)" />
    /// <typeparam name="TCommand">The command to dispatch.</typeparam>
    /// <typeparam name="TResult">What the command returns.</typeparam>
    public static Command<TCommand, TResult> Command<TCommand, TResult>(
        object? key = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
        where TCommand : ICommand<TResult> =>
        Slotted(callerFile, callerLine, key, static client => client.Command<TCommand, TResult>());

    /// <summary>
    ///     A renderable command that is a function, invalidating <paramref name="invalidates" /> after each
    ///     successful send. Inside <c>Render</c> the same call returns the same command every render.
    /// </summary>
    /// <param name="invalidates">A key prefix to refetch; a string or a type converts to one.</param>
    /// <param name="key">Identity for a command rendered in a loop; see <see cref="Command{TCommand}(object?, string, int)" />.</param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Command Command(
        QueryKey invalidates,
        object? key = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        Slotted(callerFile, callerLine, key, client => client.Command(invalidates));

    /// <summary>
    ///     A renderable command that is a function, invalidating every one of <paramref name="invalidates" />
    ///     after each successful send — none, for work that makes nothing out of date.
    /// </summary>
    /// <param name="invalidates">Key prefixes to refetch: <c>[typeof(Person), "dashboard"]</c>.</param>
    /// <param name="key">Identity for a command rendered in a loop; see <see cref="Command{TCommand}(object?, string, int)" />.</param>
    /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
    public static Command Command(
        QueryKey[] invalidates,
        object? key = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(invalidates);
        return Slotted(callerFile, callerLine, key, client => client.Command(invalidates));
    }

    // ---- one-shot calls, for a handler ---------------------------------------------------------------

    /// <inheritdoc cref="IQueryClient.SendAsync(ICommand, CancellationToken)" />
    public static Task SendAsync(ICommand command, CancellationToken cancellationToken = default) =>
        Current().SendAsync(command, cancellationToken);

    /// <inheritdoc cref="IQueryClient.SendAsync{TResult}(ICommand{TResult}, CancellationToken)" />
    public static Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Current().SendAsync(command, cancellationToken);

    /// <inheritdoc cref="IQueryClient.FetchAsync{TResult}(IQuery{TResult}, QueryOptions?, CancellationToken)" />
    public static Task<TResult> FetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Current().FetchAsync(message, options, cancellationToken);

    /// <inheritdoc cref="IQueryClient.PrefetchAsync{TResult}(IQuery{TResult}, QueryOptions?, CancellationToken)" />
    public static Task PrefetchAsync<TResult>(
        IQuery<TResult> message,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Current().PrefetchAsync(message, options, cancellationToken);

    /// <inheritdoc cref="IQueryClient.Invalidate{TQuery}" />
    /// <typeparam name="T">A query message type, or the type a <c>QueryKey.For&lt;T&gt;</c> key is about.</typeparam>
    public static void Invalidate<T>() => Current().Invalidate<T>();

    /// <inheritdoc cref="IQueryClient.Invalidate(QueryKey, bool)" />
    public static void Invalidate(QueryKey key, bool exact = false) => Current().Invalidate(key, exact);

    /// <inheritdoc cref="IQueryClient.Invalidate(Func{QueryKey, bool})" />
    public static void Invalidate(Func<QueryKey, bool> predicate) => Current().Invalidate(predicate);

    /// <inheritdoc cref="IQueryClient.InvalidateAll" />
    public static void InvalidateAll() => Current().InvalidateAll();

    /// <inheritdoc cref="IQueryClient.SetData{TResult}(IQuery{TResult}, TResult)" />
    public static void SetData<TResult>(IQuery<TResult> message, TResult data) => Current().SetData(message, data);

    /// <inheritdoc cref="IQueryClient.SetData{TResult}(QueryKey, TResult)" />
    public static void SetData<TResult>(QueryKey key, TResult data) => Current().SetData(key, data);

    // ---- plumbing ------------------------------------------------------------------------------------

    private static T Slotted<T>(string file, int line, object? key, Func<SessionQueryClient, T> create)
        where T : class
    {
        var client = Current();
        if (RenderSlots.For(file, line, key) is not { } slot)
        {
            return create(client);
        }

        if (slot.Handle is T existing)
        {
            return existing;
        }

        var created = create(client);
        slot.Handle = created;
        return created;
    }

    /// <summary>The session's cache, or a failure that says where to call from instead.</summary>
    private static SessionQueryClient Current()
    {
        var services = AmbientServices.Current
            ?? throw new InvalidOperationException(
                "QueryClient was called outside a session. Call it from a component — its constructor, "
                + "Render or an event handler — or inject IQueryClient where there is no component.");

        return services.GetService<IQueryClient>() switch
        {
            SessionQueryClient client => client,
            null => throw new InvalidOperationException(
                "QueryClient needs Rask.Query registered: call services.AddRaskQuery()."),
            _ => throw new InvalidOperationException(
                "QueryClient reaches the session's cache through the IQueryClient that AddRaskQuery() "
                + "registers, and another implementation replaced it. Inject that IQueryClient instead."),
        };
    }

    /// <summary>What a message query in <c>Render</c> was last asked for.</summary>
    private sealed record Asked<TResult>(IQuery<TResult> Message, QueryKey? Key);

    /// <summary>What a function query in <c>Render</c> last saw, typed so an unchanged input is not boxed.</summary>
    private sealed class Seen<TInput>
    {
        public QueryKey Prefix { get; set; }

        public TInput Input { get; set; } = default!;
    }
}
