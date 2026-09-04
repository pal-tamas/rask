namespace Rask.Cqrs;

// The behavior docs/cqrs.md named as the motivating example for the pipeline and then said Rask ships
// none of. It ships one now, registered first so it is the OUTERMOST wrapper: a request that is not
// valid should not reach a transaction, a log entry saying it was handled, or the handler.
//
// It resolves IEnumerable<IRequestValidator<TRequest>>, so an app with no validators for a request
// gets an empty sequence and one extra await — the cost of the feature being on by default.
/// <summary>
///     Validates every dispatched request before its handler runs, and rejects it with a
///     <see cref="RaskValidationException" /> if any rule fails.
///     <para>
///         Registered for you by <c>AddRaskCqrs</c>. An app that does without says
///         <c>app.Configure(c =&gt; c.Validation.Off())</c>, or
///         <c>AddRaskCqrs(o =&gt; o.ValidateRequests = false)</c> when it is not using the
///         <c>Rask</c> package.
///     </para>
/// </summary>
/// <typeparam name="TRequest">The request being dispatched.</typeparam>
/// <typeparam name="TResult">What its handler returns; <see cref="Unit" /> for a void command.</typeparam>
public sealed class ValidationBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    ///     Creates the behavior over whatever validators are registered for this request type.
    /// </summary>
    /// <param name="validators">The validators, resolved from the dispatch scope.</param>
    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators;
    }

    /// <inheritdoc />
    public async Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        List<RequestValidationError>? errors = null;

        // Every validator runs, rather than stopping at the first that fails. A caller fixing a request
        // wants the whole list — the form pipeline's first-error-wins is a UX affordance for a field
        // being typed into, and a request is not being typed into.
        foreach (var validator in _validators)
        {
            var found = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (found is null || found.Count == 0)
            {
                continue;
            }

            errors ??= [];
            errors.AddRange(found);
        }

        if (errors is { Count: > 0 })
        {
            throw new RaskValidationException(errors);
        }

        return await next().ConfigureAwait(false);
    }
}
