namespace Rask.Cqrs;

/// <summary>
/// Dispatches queries and commands to their single handler, and (via <see cref="IPublisher"/>)
/// publishes notifications. Inject it and call
/// <see cref="DispatchAsync{TResult}(IQuery{TResult}, CancellationToken)"/> /
/// <see cref="DispatchAsync(ICommand, CancellationToken)"/> — the result type is inferred from the
/// message — or <see cref="IPublisher.PublishAsync{TNotification}"/>. Inject the narrower
/// <see cref="IPublisher"/> for a publish-only surface. Backed by a source-generated, reflection-free
/// dispatch map.
/// </summary>
public interface IDispatcher : IPublisher
{
    /// <summary>
    /// Dispatches a query to its <see cref="IQueryHandler{TQuery, TResult}"/>. The result type is
    /// inferred from the query's <see cref="IQuery{TResult}"/> interface.
    /// </summary>
    Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a void command to its <see cref="ICommandHandler{TCommand}"/>.</summary>
    Task DispatchAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a command to its <see cref="ICommandHandler{TCommand, TResult}"/>. The result type is
    /// inferred from the command's <see cref="ICommand{TResult}"/> interface.
    /// </summary>
    Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}

/// <summary>Publishes notifications to every registered handler.</summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to every <see cref="INotificationHandler{TNotification}"/> registered for
    /// its <b>concrete runtime type</b>, using the strategy configured on <see cref="CqrsOptions"/>.
    /// Handlers declared against a base type are not invoked, and a notification with no handlers is a
    /// no-op.
    /// </summary>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
