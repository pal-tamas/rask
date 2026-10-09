using Rask.Wire;

namespace Rask.Cqrs;

/// <summary>
///     The <see cref="RemoteDispatchException" /> for a request the server REJECTED — a 400 whose problem
///     document carries field errors — which a form can therefore show under its fields.
/// </summary>
/// <remarks>
///     <para>
///         A type of its own, and not an interface on every remote failure: a form takes anything that is an
///         <see cref="IFieldFailures" /> as "shown under its fields, nothing else wrong", so a 500 or a dead
///         connection must never be one.
///     </para>
///     <para>
///         The failures are rebuilt from the <c>errors</c> dictionary, which is all the wire carries. Each
///         distinct message becomes one failure over every field that holds it. What is lost against the
///         server's own failures: fields that were only marked, what found the failure, and the difference
///         between one rule over two fields and two rules that happen to share a message.
///     </para>
/// </remarks>
internal sealed class RejectedRemoteDispatchException : RemoteDispatchException, IFieldFailures
{
    private static readonly Dictionary<string, string[]> NoErrors = new(StringComparer.Ordinal);

    public RejectedRemoteDispatchException()
        : this("The server rejected the request.")
    {
    }

    public RejectedRemoteDispatchException(string message)
        : this(message, NoErrors)
    {
    }

    public RejectedRemoteDispatchException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Errors = NoErrors;
        Failures = [new FieldFailure(message, [])];
    }

    // Errors is set here, from the same dictionary the failures are rebuilt from, so the two cannot disagree.
    // Never empty: a form reads "no failures" as "nothing wrong".
    internal RejectedRemoteDispatchException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors;
        Failures = FieldFailureMap.FromDictionary(errors) is { Count: > 0 } rebuilt
            ? rebuilt
            : [new FieldFailure(message, [])];
    }

    /// <inheritdoc />
    public IReadOnlyList<FieldFailure> Failures { get; }
}
