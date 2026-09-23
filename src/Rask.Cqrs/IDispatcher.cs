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
    /// every open subscription for it — <see cref="SubscribeAsync{TNotification}"/>, or a component's
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
    /// For a notification marked <see cref="ForAttribute{TScope}"/>, pass the key of the thing to watch: only
    /// notifications about it arrive, and the <see cref="IWatchPolicy{TScope}"/> is asked first — a refusal
    /// throws <see cref="UnauthorizedAccessException"/> before anything is delivered.
    /// </para>
    /// <code>
    /// await foreach (var shipped in dispatcher.SubscribeAsync&lt;OrderShipped&gt;(orderId, ct))
    ///     Console.WriteLine(shipped.Status);
    /// </code>
    /// <para>
    /// On a client of a server (<c>AddRaskCqrsClient</c>) the subscription is opened on the server, so it hears what
    /// every visitor's command published there; elsewhere it hears the notifications its own process publishes.
    /// A component renders one through <c>QueryClient.Subscribe</c> instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="TNotification">The notification to watch.</typeparam>
    /// <param name="key">What to watch, for a scoped notification; null for one that goes to everyone.</param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    IAsyncEnumerable<TNotification> SubscribeAsync<TNotification>(
        object? key = null,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
