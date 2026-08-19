namespace Rask.Cqrs;

/// <summary>
///     Thrown when a message dispatched to a remote handler could not be completed — the request never
///     arrived, the server refused it, or the handler failed there.
/// </summary>
/// <remarks>
///     <para>
///         A remote dispatch has failure modes an in-process one does not: the network, the status
///         code, and a handler exception that must not be reconstructed on the caller's side (doing so
///         would need the exception's type at runtime, and would leak server internals to a browser).
///         So the server maps a handler failure to a problem document and the client raises this,
///         carrying what is safe to know: which message, what status, and the problem's stable type.
///     </para>
///     <para>
///         Catch it the way you would catch any dispatch failure. Application-level outcomes that the
///         caller is expected to branch on — "not found", "already taken" — belong in the message's
///         result type rather than here; an exception is for the call not completing.
///     </para>
/// </remarks>
public sealed class RemoteDispatchException : Exception
{
    /// <summary>Creates an exception with a default message.</summary>
    public RemoteDispatchException()
        : base("A remotely dispatched message failed.")
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">The description of the failure.</param>
    public RemoteDispatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">The description of the failure.</param>
    /// <param name="innerException">The underlying transport failure, when there was one.</param>
    public RemoteDispatchException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     The wire name of the message that failed — the same name that appears in the request path,
    ///     so a client-side log line and a server-side one can be lined up.
    /// </summary>
    public string? MessageName { get; init; }

    /// <summary>
    ///     The HTTP status the server answered with, or null when the request never got a response
    ///     (offline, DNS failure, timeout). Null is the signal that <see cref="Exception.InnerException" />
    ///     holds the transport failure.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    ///     The problem document's <c>type</c> URI when the server sent one. Stable across releases, so
    ///     it is the right thing to branch on — unlike the human-readable message.
    /// </summary>
    public string? ProblemType { get; init; }

    /// <summary>
    ///     The problem document's <c>detail</c>, when the server chose to send one. A server does not
    ///     send handler exception text in production, so expect this to be null there.
    /// </summary>
    public string? Detail { get; init; }
}
