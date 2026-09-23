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
/// Marks an event: something that has happened. Publishing it through
/// <see cref="IDispatcher.PublishAsync{TNotification}"/> runs every
/// <see cref="INotificationHandler{TNotification}"/> for it <b>and</b> hands it to every open subscription —
/// <see cref="IDispatcher.SubscribeAsync{TNotification}"/>, or a component's <c>QueryClient.Subscribe</c> — so one
/// record reaches the code that reacts and the screens that show it.
/// </summary>
/// <remarks>
/// Mark the property that says which thing the event is about with <see cref="ForAttribute{TScope}"/> and it reaches
/// only the subscribers watching that thing, each admitted by an <see cref="IWatchPolicy{TScope}"/>:
/// <code>
/// public sealed record OrderShipped([For&lt;Order&gt;] Guid OrderId, string Status) : INotification;
/// </code>
/// </remarks>
public interface INotification;
