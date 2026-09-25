namespace Rask.Core.Diagnostics;

/// <summary>
///     Severity of a <see cref="RaskDiagnosticEvent" />. Deliberately a small subset that maps
///     cleanly onto <c>Microsoft.Extensions.Logging.LogLevel</c> when a host bridges the
///     <see cref="RaskDiagnostics.Sink" /> to an <c>ILogger</c>:
///     <see cref="Information" />→<c>Information</c>, <see cref="Warning" />→<c>Warning</c>,
///     <see cref="Error" />→<c>Error</c>.
/// </summary>
internal enum RaskLogLevel
{
    Information,
    Warning,
    Error
}
