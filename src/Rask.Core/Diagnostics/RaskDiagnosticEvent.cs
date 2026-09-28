namespace Rask.Core.Diagnostics;

/// <summary>
///     A single framework diagnostic — a faulting lifecycle hook, a dispose that threw, a duplicate
///     <c>data-rask-key</c>, a JS-invoke fault, and so on. Carries the human-readable
///     <see cref="Message" /> and the <see cref="Exception" /> separately (rather than baking the
///     exception into the message text) so a structured sink can log them as distinct fields.
/// </summary>
internal readonly struct RaskDiagnosticEvent(
    RaskLogLevel level,
    string category,
    string message,
    Exception? exception = null)
{
    /// <summary>Severity of the event.</summary>
    public RaskLogLevel Level { get; } = level;

    /// <summary>
    ///     Stable subsystem category (e.g. <c>Rask.Lifecycle</c>, <c>Rask.Diff</c>,
    ///     <c>Rask.JsInvoke</c>). Maps to the <c>ILogger</c> category on the host side.
    /// </summary>
    public string Category { get; } = category;

    /// <summary>Human-readable message, <em>without</em> the exception text appended.</summary>
    public string Message { get; } = message;

    /// <summary>The associated exception, or <c>null</c> for a message-only diagnostic.</summary>
    public Exception? Exception { get; } = exception;
}
