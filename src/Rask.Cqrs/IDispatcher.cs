namespace Rask.Cqrs;

/// <summary>
/// The single entry point for Rask.Cqrs: dispatches queries and commands to their one handler and
/// publishes notifications to every handler. Inject it and call
/// <see cref="QueryAsync{TResult}(IQuery{TResult}, CancellationToken)"/> /
/// <see cref="SendAsync(ICommand, CancellationToken)"/> — the result type is inferred from the
/// message — or <see cref="PublishAsync{TNotification}"/>. Backed by a source-generated,
/// reflection-free dispatch map.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Dispatches a query to its <see cref="IQueryHandler{TQuery, TResult}"/>. The result type is
    /// inferred from the query's <see cref="IQuery{TResult}"/> interface.
    /// </summary>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a void command to its <see cref="ICommandHandler{TCommand}"/>.</summary>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a command to its <see cref="ICommandHandler{TCommand, TResult}"/>. The result type is
    /// inferred from the command's <see cref="ICommand{TResult}"/> interface.
    /// </summary>
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to every <see cref="INotificationHandler{TNotification}"/> registered for
    /// its <b>concrete runtime type</b>, using the strategy configured on <see cref="CqrsOptions"/>, and to
    /// every open subscription for it — <c>SubscribeAsync</c>, or a component's
    /// <c>QueryClient.Subscribe</c>. Handlers declared against a base type are not invoked, and a
    /// notification with neither handlers nor subscribers goes nowhere.
    /// </summary>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Every <typeparamref name="TNotification"/> published from now on — starting with the last one, when
    /// there is one — until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This form watches the type itself, so it hears every one published. To watch the events about one thing
    /// — this order, this user's export — pass an <see cref="ISubscription{TNotification}"/> record instead.
    /// </para>
    /// <code>
    /// await foreach (var placed in dispatcher.SubscribeAsync&lt;OrderPlaced&gt;(ct))
    ///     Console.WriteLine(placed.Number);
    /// </code>
    /// </remarks>
    /// <typeparam name="TNotification">The notification to watch.</typeparam>
    /// <param name="cancellationToken">Ends the subscription.</param>
    IAsyncEnumerable<TNotification> SubscribeAsync<TNotification>(CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// The <typeparamref name="TNotification"/>s <paramref name="subscription"/> asks for — starting with the
    /// last matching one, when there is one — until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record says which notifications are its own, and an <see cref="IWatchPolicy{TSubscription}"/> says who
    /// may open it — asked once, before anything is delivered; a refusal throws
    /// <see cref="UnauthorizedAccessException"/>, and with no policy registered nobody may.
    /// </para>
    /// <code>
    /// await foreach (var shipped in dispatcher.SubscribeAsync(new WatchOrder(orderId), ct))
    ///     Console.WriteLine(shipped.Status);
    /// </code>
    /// <para>
    /// On a client of a server (<c>AddRaskCqrsClient</c>) the subscription is opened on the server, so it hears what
    /// every visitor's command published there; elsewhere it hears the notifications its own process publishes.
    /// A component renders one through <c>QueryClient.Subscribe</c> instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="TNotification">The notification the subscription carries.</typeparam>
    /// <param name="subscription">What to watch.</param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    IAsyncEnumerable<TNotification> SubscribeAsync<TNotification>(
        ISubscription<TNotification> subscription,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
