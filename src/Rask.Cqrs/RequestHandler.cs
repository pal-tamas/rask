namespace Rask.Cqrs;

/// <summary>
/// Invokes the next stage of the dispatch pipeline — either the next behavior or, at the innermost
/// layer, the request handler itself.
/// </summary>
/// <typeparam name="TResult">The result type flowing through the pipeline.</typeparam>
public delegate Task<TResult> RequestHandler<TResult>();
