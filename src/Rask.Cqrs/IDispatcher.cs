namespace Rask.Cqrs;

/// <summary>
/// Dispatches queries to their single <see cref="IQueryHandler{TQuery, TResult}"/>. Inject this when a
/// component or service only reads — keeping the read side separate from the write side is the point of
/// CQRS.
/// </summary>
public interface IQueryDispatcher
{
    /// <summary>
    /// Dispatches a query to its handler. The result type is inferred from the query's
    /// <see cref="IQuery{TResult}"/> interface.
    /// </summary>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches commands to their single command handler. Inject this when a component or service only
/// writes.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>Dispatches a void command to its <see cref="ICommandHandler{TCommand}"/>.</summary>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a command to its <see cref="ICommandHandler{TCommand, TResult}"/>. The result type is
    /// inferred from the command's <see cref="ICommand{TResult}"/> interface.
    /// </summary>
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
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

/// <summary>
/// The umbrella entry point that combines <see cref="IQueryDispatcher"/>, <see cref="ICommandDispatcher"/>
/// and <see cref="IPublisher"/>. Inject the fine-grained interface for a read- or write-only surface, or
/// this one when a type does both. Backed by a source-generated, reflection-free dispatch map.
/// </summary>
public interface IDispatcher : IQueryDispatcher, ICommandDispatcher, IPublisher;
