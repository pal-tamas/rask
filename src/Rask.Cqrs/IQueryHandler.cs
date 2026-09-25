namespace Rask.Cqrs;

/// <summary>Handles a single <see cref="IQuery{TResult}"/> type.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result the query returns.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Executes the query. Cancelled with the work that sent it — read <c>Current.Cancellation</c> to pass on.</summary>
    Task<TResult> Handle(TQuery query);
}
