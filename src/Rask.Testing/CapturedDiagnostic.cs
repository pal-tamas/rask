namespace Rask.Testing;

/// <summary>One framework diagnostic, as captured by <see cref="CapturingDiagnostics" />.</summary>
/// <param name="Level">Severity.</param>
/// <param name="Category">The subsystem that raised it, e.g. <c>Rask.Lifecycle</c>.</param>
/// <param name="Message">The human-readable message, without the exception text appended.</param>
/// <param name="Exception">The associated exception, or <c>null</c> for a message-only diagnostic.</param>
public sealed record CapturedDiagnostic(
    DiagnosticLevel Level,
    string Category,
    string Message,
    Exception? Exception);
