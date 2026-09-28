namespace Rask.Cqrs;

/// <summary>
/// Marks a request that asks for data and returns a <typeparamref name="TResult"/> without mutating
/// state. Handled by a single <see cref="IQueryHandler{TQuery, TResult}"/>. Dispatch it through
/// <see cref="IDispatcher.Query{TResult}(IQuery{TResult}, System.Threading.CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResult">The type the query returns.</typeparam>
#pragma warning disable S2326 // TResult is a phantom: it is what the dispatcher infers the result type from
public interface IQuery<out TResult>;
#pragma warning restore S2326
