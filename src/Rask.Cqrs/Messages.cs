namespace Rask.Cqrs;

/// <summary>
/// Marks a request that asks for data and returns a <typeparamref name="TResult"/> without mutating
/// state. Handled by a single <see cref="IQueryHandler{TQuery, TResult}"/>. Dispatch it through
/// <see cref="IDispatcher.QueryAsync{TResult}(IQuery{TResult}, System.Threading.CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResult">The type the query returns.</typeparam>
public interface IQuery<out TResult>;

/// <summary>
/// Marks a request that performs a side effect and returns no value. Handled by a single
/// <see cref="ICommandHandler{TCommand}"/>. Dispatch it through <see cref="IDispatcher.SendAsync(ICommand, System.Threading.CancellationToken)"/>.
/// </summary>
public interface ICommand;

/// <summary>
/// Marks a request that performs a side effect and returns a <typeparamref name="TResult"/> (for
/// example the identifier of a newly created entity). Handled by a single
/// <see cref="ICommandHandler{TCommand, TResult}"/>. Dispatch it through
/// <see cref="IDispatcher.SendAsync{TResult}(ICommand{TResult}, System.Threading.CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResult">The type the command returns.</typeparam>
public interface ICommand<out TResult>;

/// <summary>
/// Marks an event that is broadcast to zero or more <see cref="INotificationHandler{TNotification}"/>
/// instances. Publish it through <see cref="IDispatcher.PublishAsync{TNotification}"/>.
/// </summary>
public interface INotification;
