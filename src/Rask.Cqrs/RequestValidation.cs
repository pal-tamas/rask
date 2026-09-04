namespace Rask.Cqrs;

/// <summary>
///     One thing wrong with a request: the field it is about, and what to say about it.
/// </summary>
/// <param name="Field">
///     The request property the failure belongs to. Empty for a rule about the request as a whole.
/// </param>
/// <param name="Message">The message, written for whoever sent the request.</param>
public readonly record struct RequestValidationError(string Field, string Message);

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
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Every failure found; empty when the request is valid.</returns>
    ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        TRequest request, CancellationToken cancellationToken);
}

// The non-generic shape, for the one caller that cannot use the generic one.
//
// A client replaces the generated invoker with a remote one (AddRaskCqrsClient), so the pipeline — and
// with it ValidationBehavior — is bypassed for anything that travels. The remote transport sees the
// message as `object`, and resolving IRequestValidator<TRequest> from that would mean MakeGenericType:
// reflection, in the one package whose whole point is that it has none and publishes trim-clean.
/// <summary>
///     Validates a request that is about to be sent to a server, before it is sent.
///     <para>
///         Registered for you. This is a convenience, not a control — the server validates again, and
///         that run is the one that decides.
///     </para>
/// </summary>
public interface IRemoteRequestValidator
{
    /// <summary>Checks a request about to leave for the server.</summary>
    /// <param name="request">The query or command being sent.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Every failure found; empty when the request is valid.</returns>
    ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        object request, CancellationToken cancellationToken);
}

// Thrown rather than returned. A behavior short-circuits by not calling next(), but it still has to
// produce a TResult, and there is no value of an arbitrary TResult that means "this did not happen" —
// inventing one would make every handler's result type carry a case it never asked for. The server
// endpoint turns this into a 400 with the field errors intact; in-process it reaches the caller as
// itself.
/// <summary>
///     A request was rejected before its handler ran, because it failed validation.
/// </summary>
public sealed class RaskValidationException : Exception
{
    /// <summary>
    ///     Creates the exception from the failures that caused it.
    /// </summary>
    /// <param name="errors">Every failure, in the order the validators produced them.</param>
    public RaskValidationException(IReadOnlyList<RequestValidationError> errors)
        : base(Describe(errors))
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = Group(errors);
    }

    /// <summary>
    ///     The failures, grouped by field. The empty key holds rules about the request as a whole.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static Dictionary<string, string[]> Group(IReadOnlyList<RequestValidationError> errors)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            if (!grouped.TryGetValue(error.Field, out var list))
            {
                list = [];
                grouped[error.Field] = list;
            }

            list.Add(error.Message);
        }

        return grouped.ToDictionary(static kv => kv.Key, static kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }

    // The Message is for an operator reading a log, so it names the fields. The messages themselves go
    // to the caller through Errors, which is what the endpoint writes — this text is never the wire
    // format.
    private static string Describe(IReadOnlyList<RequestValidationError> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return "The request failed validation.";
        }

        var fields = errors
            .Select(static e => e.Field.Length == 0 ? "(request)" : e.Field)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return $"The request failed validation: {string.Join(", ", fields)}.";
    }
}
