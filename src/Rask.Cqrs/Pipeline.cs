namespace Rask.Cqrs;

/// <summary>
/// Invokes the next stage of the dispatch pipeline — either the next behavior or, at the innermost
/// layer, the request handler itself.
/// </summary>
/// <typeparam name="TResult">The result type flowing through the pipeline.</typeparam>
public delegate Task<TResult> RequestHandlerDelegate<TResult>();

/// <summary>
/// A decorator that wraps the handling of a request. Behaviors are the extension point for
/// cross-cutting concerns (logging, validation, transactions, caching, retries); Rask.Cqrs ships
/// none, you implement your own. Behaviors run as an onion in <b>registration order</b> — the
/// first-registered wraps outermost. Call <c>next</c> to continue the pipeline, or
/// return without calling it to short-circuit.
/// </summary>
/// <typeparam name="TRequest">
/// The request type. A void <see cref="ICommand"/> flows through as <c>TResult = </c>
/// <see cref="Unit"/>.
/// </typeparam>
/// <typeparam name="TResult">The result type the request produces.</typeparam>
public interface IPipelineBehavior<in TRequest, TResult>
{
    /// <summary>Wraps <paramref name="next"/> around the request.</summary>
    Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}
