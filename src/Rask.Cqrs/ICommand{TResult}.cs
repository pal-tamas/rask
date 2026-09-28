namespace Rask.Cqrs;

/// <summary>
/// Marks a request that performs a side effect and returns a <typeparamref name="TResult"/> (for
/// example the identifier of a newly created entity). Handled by a single
/// <see cref="ICommandHandler{TCommand, TResult}"/>. Dispatch it through
/// <see cref="IDispatcher.Send{TResult}(ICommand{TResult}, System.Threading.CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResult">The type the command returns.</typeparam>
#pragma warning disable S2326 // TResult is a phantom: it is what the dispatcher infers the result type from
public interface ICommand<out TResult>;
#pragma warning restore S2326
