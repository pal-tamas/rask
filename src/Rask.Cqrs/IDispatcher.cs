namespace Rask.Cqrs;

/// <summary>
/// The single entry point for Rask.Cqrs: dispatches queries and commands to their one handler and
/// publishes notifications to every handler. Inject it and call
/// <see cref="Query{TResult}(IQuery{TResult}, CancellationToken)"/> /
/// <see cref="Send(ICommand, CancellationToken)"/> — the result type is inferred from the
/// message — or <see cref="Publish{TNotification}"/>. Backed by a source-generated,
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
    /// Publishes a notification to every <see cref="INotificationHandler{TNotification}"/> registered for
    /// its <b>concrete runtime type</b>, using the strategy configured on <see cref="CqrsOptions"/>.
    /// Handlers declared against a base type are not invoked, and a notification with no handlers is a
    /// no-op.
    /// </summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
