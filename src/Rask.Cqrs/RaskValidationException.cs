namespace Rask.Cqrs;

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
    /// <summary>Creates the exception with no field failures.</summary>
    public RaskValidationException()
        : this("The request failed validation.")
    {
    }

    /// <summary>Creates the exception with a message and no field failures.</summary>
    /// <param name="message">What went wrong.</param>
    public RaskValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    /// <summary>Creates the exception with a message, the exception that caused it, and no field failures.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public RaskValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

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
    ///     Creates the exception from the failures that caused it and the exception they were read from.
    /// </summary>
    /// <param name="errors">Every failure, in the order they were found.</param>
    /// <param name="innerException">
    ///     What actually failed — a database's refusal of a duplicate, say. It is for the log: only
    ///     <see cref="Errors" /> is ever shown to the caller.
    /// </param>
    public RaskValidationException(IReadOnlyList<RequestValidationError> errors, Exception innerException)
        : base(Describe(errors), innerException)
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
