using Rask.Wire;

namespace Rask.Cqrs;

// Thrown rather than returned. A behavior short-circuits by not calling next(), but it still has to
// produce a TResult, and there is no value of an arbitrary TResult that means "this did not happen" —
// inventing one would make every handler's result type carry a case it never asked for. The server
// endpoint turns this into a 400 with the field errors intact; in-process it reaches the caller as
// itself.
/// <summary>
///     A request was rejected: it failed validation before its handler ran, or the database refused what it
///     asked for by a rule that says so.
/// </summary>
/// <remarks>
///     It names the fields it is about (<see cref="IFieldFailures" />), so a form whose submit handler lets it
///     through shows each message under its field instead of failing the submit.
/// </remarks>
public sealed class RaskValidationException : Exception, IFieldFailures
{
    private const string Unspecified = "The request failed validation.";

    /// <summary>Creates the exception with no field failures.</summary>
    public RaskValidationException()
        : this(Unspecified)
    {
    }

    /// <summary>Creates the exception with a message and no field failures.</summary>
    /// <param name="message">What went wrong.</param>
    public RaskValidationException(string message)
        : base(message)
    {
        Failures = AboutTheWhole(message);
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    /// <summary>Creates the exception with a message, the exception that caused it, and no field failures.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public RaskValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = AboutTheWhole(message);
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Creates the exception from the failures that caused it.
    /// </summary>
    /// <param name="errors">Every failure, in the order the validators produced them.</param>
    public RaskValidationException(IReadOnlyList<RequestValidationError> errors)
        : this(FieldFailureMap.FromErrors(errors ?? throw new ArgumentNullException(nameof(errors))))
    {
    }

    /// <summary>
    ///     Creates the exception from the failures that caused it and the exception they were read from.
    /// </summary>
    /// <param name="errors">Every failure, in the order they were found.</param>
    /// <param name="innerException">
    ///     What actually failed — a database's refusal of a duplicate, say. It is for the log: only
    ///     <see cref="Errors" /> is ever shown to the caller.
    /// </param>
    public RaskValidationException(IReadOnlyList<RequestValidationError> errors, Exception innerException)
        : this(FieldFailureMap.FromErrors(errors ?? throw new ArgumentNullException(nameof(errors))), innerException)
    {
    }

    /// <summary>
    ///     Creates the exception from failures that each name every field they are about.
    /// </summary>
    /// <param name="failures">Every failure, in the order they were found.</param>
    /// <param name="innerException">What actually failed, for the log; or null.</param>
    /// <remarks>
    ///     The form of the others that keeps a failure whole: one message over several fields — a unique index
    ///     over a year and a number — is ONE failure here, where <see cref="Errors" /> can only repeat it under
    ///     each.
    /// </remarks>
    public RaskValidationException(IReadOnlyList<FieldFailure> failures, Exception? innerException = null)
        : base(Describe(failures ?? throw new ArgumentNullException(nameof(failures))), innerException)
    {
        Failures = failures.Count == 0 ? AboutTheWhole(Unspecified) : failures;
        Errors = FieldFailureMap.ToDictionary(failures);
    }

    /// <summary>
    ///     The failures, grouped by field: each message under every field its failure names. The empty key
    ///     holds rules about the request as a whole.
    /// </summary>
    /// <remarks>The shape that crosses the wire. <see cref="Failures" /> is the same thing, ungrouped.</remarks>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Never empty: an exception that named no field still says what it says about the submission as a
    ///     whole, so a form that shows field failures cannot take it for "nothing wrong".
    /// </remarks>
    public IReadOnlyList<FieldFailure> Failures { get; }

    private static FieldFailure[] AboutTheWhole(string message) => [new FieldFailure(message, [])];

    // The Message is for an operator reading a log, so it names the fields. The messages themselves go
    // to the caller through Errors, which is what the endpoint writes — this text is never the wire
    // format.
    private static string Describe(IReadOnlyList<FieldFailure> failures)
    {
        if (failures.Count == 0)
        {
            return Unspecified;
        }

        var fields = failures
            .SelectMany(static f => f.Fields is { Count: > 0 } named ? named : ["(request)"])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return $"The request failed validation: {string.Join(", ", fields)}.";
    }
}
