namespace Rask.Cqrs;

/// <summary>
/// The single entry point for Rask.Cqrs: dispatches queries and commands to their one handler and
/// publishes events to every handler. Inject it and call
/// <see cref="Query{TResult}(IQuery{TResult}, CancellationToken)"/> /
/// <see cref="Send(ICommand, CancellationToken)"/> — the result type is inferred from the
/// message — or <see cref="Publish{TEvent}"/>. Backed by a source-generated,
/// reflection-free dispatch map.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Dispatches a query to its <see cref="IQueryHandler{TQuery, TResult}"/>. The result type is
    /// inferred from the query's <see cref="IQuery{TResult}"/> interface.
    /// </summary>
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a void command to its <see cref="ICommandHandler{TCommand}"/>.</summary>
    Task Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a command to its <see cref="ICommandHandler{TCommand, TResult}"/>. The result type is
    /// inferred from the command's <see cref="ICommand{TResult}"/> interface.
    /// </summary>
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to every <see cref="IEventHandler{TEvent}"/> registered for
    /// its <b>concrete runtime type</b>, using the strategy configured on <see cref="CqrsOptions"/>, and to
    /// every open subscription for it — <c>Subscribe</c>, or a component's
    /// <c>QueryClient.Subscribe</c>. Handlers declared against a base type are not invoked, and a
    /// event with neither handlers nor subscribers goes nowhere.
    /// </summary>
    Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Every <typeparamref name="TEvent"/> published from now on — starting with the last one, when
    /// there is one — until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This form watches the type itself, so it hears every one published. To watch the events about one thing
    /// — this order, this user's export — pass an <see cref="ISubscription{TEvent}"/> record instead.
    /// </para>
    /// <code>
    /// await foreach (var placed in dispatcher.Subscribe&lt;OrderPlaced&gt;(ct))
    ///     Console.WriteLine(placed.Number);
    /// </code>
    /// </remarks>
    /// <typeparam name="TEvent">The event to watch.</typeparam>
    /// <param name="cancellationToken">Ends the subscription.</param>
    IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// The <typeparamref name="TEvent"/>s <paramref name="subscription"/> asks for — starting with the
    /// last matching one, when there is one — until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record says which events are its own, and an <see cref="IWatchPolicy{TSubscription}"/> says who
    /// may open it — asked once, before anything is delivered; a refusal throws
    /// <see cref="UnauthorizedAccessException"/>, and with no policy registered nobody may.
    /// </para>
    /// <code>
    /// await foreach (var shipped in dispatcher.Subscribe(new WatchOrder(orderId), ct))
    ///     Console.WriteLine(shipped.Status);
    /// </code>
    /// <para>
    /// On a client of a server (<c>AddRaskCqrsClient</c>) the subscription is opened on the server, so it hears what
    /// every visitor's command published there; elsewhere it hears the events its own process publishes.
    /// A component renders one through <c>QueryClient.Subscribe</c> instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEvent">The event the subscription carries.</typeparam>
    /// <param name="subscription">What to watch.</param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    IAsyncEnumerable<TEvent> Subscribe<TEvent>(
        ISubscription<TEvent> subscription,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
