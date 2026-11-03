using Rask.Wire;

namespace Rask;

/// <summary>
///     An expected refusal, whose message is written for the person who asked and is shown to them.
/// </summary>
/// <remarks>
///     <code>
///     throw new RefusedException("The invoice is already closed.");
///     </code>
///     <para>
///         Every other exception's message stays hidden, because it is written for whoever reads the log. This one
///         is the app saying no on purpose, from anywhere — a page, a handler, a command, an endpoint — and its
///         message is said where the person is reading: in the handler's toast in place of "That did not work",
///         in a form's own messages, on the error page and in an <c>ErrorBoundary</c> with no <c>Fallback</c>
///         in place of the reference sentence, and as the <c>detail</c> of a 400 problem on a request that is
///         not a page.
///     </para>
///     <para>
///         It is not a fault: it is logged at Information, with no reference to quote. Derive from it to give a
///         refusal its own status — <c>server.Answer&lt;InvoiceClosedException&gt;(409)</c> — and the
///         message is still shown.
///     </para>
/// </remarks>
public class RefusedException : Exception, IFieldFailures
{
    private const string Unspecified = "The request was refused.";

    /// <summary>Creates the refusal with a generic sentence.</summary>
    public RefusedException()
        : this(Unspecified)
    {
    }

    /// <summary>Creates the refusal with the sentence the person is shown.</summary>
    /// <param name="message">What was refused, written for the person who asked.</param>
    public RefusedException(string message)
        : base(message ?? Unspecified)
    {
        Failures = [new FieldFailure(message ?? Unspecified, [])];
    }

    /// <summary>Creates the refusal with the sentence the person is shown and the exception that caused it.</summary>
    /// <param name="message">What was refused, written for the person who asked.</param>
    /// <param name="innerException">What led to the refusal, for the log only.</param>
    public RefusedException(string message, Exception innerException)
        : base(message ?? Unspecified, innerException)
    {
        Failures = [new FieldFailure(message ?? Unspecified, [])];
    }

    /// <inheritdoc />
    /// <remarks>One failure about the request as a whole, carrying the message.</remarks>
    public IReadOnlyList<FieldFailure> Failures { get; }
}
