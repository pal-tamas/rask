namespace Rask.Cqrs;

/// <summary>
///     Validates a dispatched request before its handler runs.
///     <para>
///         Rask supplies these for you — a request's <c>System.ComponentModel.DataAnnotations</c>
///         attributes and any <c>AbstractValidator&lt;T&gt;</c> you wrote for it are both surfaced as
///         validators. Implement it yourself for a rule that fits neither.
///     </para>
///     <para>
///         Asynchronous by construction: a rule that has to ask a database whether a name is taken is
///         the common case, not the exception, so there is no synchronous shape to reach for first and
///         regret later.
///     </para>
/// </summary>
/// <typeparam name="TRequest">The query or command this validates.</typeparam>
public interface IRequestValidator<in TRequest>
{
    /// <summary>Checks the request.</summary>
    /// <param name="request">The request about to be handled.</param>
    /// <returns>Every failure found; empty when the request is valid.</returns>
    ValueTask<IReadOnlyList<RequestValidationError>> Validate(TRequest request);
}
