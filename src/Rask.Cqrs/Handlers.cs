namespace Rask.Cqrs;

/// <summary>Handles a single <see cref="IQuery{TResult}"/> type.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result the query returns.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Executes the query.</summary>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Handles a single void <see cref="ICommand"/> type.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Executes the command.</summary>
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles a single <see cref="ICommand{TResult}"/> type that returns a value.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result the command returns.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Executes the command and returns its result.</summary>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles an <see cref="INotification"/>. Any number of handlers may exist for one notification;
/// all of them run when it is published.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Reacts to the published notification.</summary>
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken);
}
