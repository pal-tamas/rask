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

/// <summary>
///     The framework's dependency-free diagnostic seam. <c>Rask.Core</c> (and the WASM host) deliberately
///     take no dependency on <c>Microsoft.Extensions.Logging</c>, yet still need to surface faults that
///     would otherwise be swallowed — a lifecycle hook that threw with no ancestor
///     <see cref="Rask.Core.Components.ErrorBoundary" />, a component <c>Dispose</c> that faulted, a duplicate sibling key, a
///     JS-invoke fault. Those sites call <see cref="Report" /> / <see cref="ReportOnce" /> instead of
///     writing to <see cref="Console.Error" /> directly.
///     <para>
///         The default <see cref="Sink" /> reproduces the historical <c>Console.Error</c> behaviour, so an
///         unconfigured app is unchanged. A host (via the server/WASM hosting layer, which can see these
///         internals) bridges <see cref="Sink" /> once at startup to route every framework diagnostic into
///         its own <c>ILogger</c> / metrics pipeline; a test swaps in a capturing sink and restores the
///         previous one afterwards. Kept <c>internal</c> for now: the public, documented host-facing knob
///         is introduced by the server-side observability layer that consumes this seam.
///     </para>
/// </summary>
internal static class RaskDiagnostics
{
    // Bounds ReportOnce's memory: a correct app never populates this, and a buggy one that churns
    // unbounded distinct dedup keys stops being reported past the cap rather than growing without
    // limit. The budget is shared across all ReportOnce call sites; today only the live diff's
    // duplicate-key warning uses it, so the historical per-key 1024 cap is preserved exactly.
    private const int ReportOnceCap = 1024;
    private static readonly HashSet<string> ReportedOnce = new(StringComparer.Ordinal);

    /// <summary>
    ///     Where framework diagnostics go. Defaults to a <see cref="Console.Error" /> writer (matching the
    ///     framework's prior <c>"…: {ex}"</c> stderr format). A host bridges this to structured logging; a
    ///     test swaps in a capturing sink and restores the previous value. Set to <c>null</c> to silence
    ///     framework diagnostics entirely. Read into a local before invocation, so reassigning it
    ///     concurrently with a <see cref="Report" /> is safe.
    /// </summary>
    public static Action<RaskDiagnosticEvent>? Sink { get; set; } = WriteToStandardError;

    /// <summary>
    ///     Surface a framework diagnostic through the active <see cref="Sink" />. A no-op when the sink
    ///     is <c>null</c>.
    /// </summary>
    public static void Report(RaskLogLevel level, string category, string message, Exception? exception = null)
    {
        // Snapshot the settable sink so a concurrent reassignment can't null it out mid-call.
        var sink = Sink;
        sink?.Invoke(new RaskDiagnosticEvent(level, category, message, exception));
    }

    /// <summary>
    ///     Surface a diagnostic at most once per distinct <paramref name="dedupKey" />, bounded to
    ///     <see cref="ReportOnceCap" /> distinct keys. <paramref name="messageFactory" /> is invoked only
    ///     when the event is actually delivered, so a condition that recurs every render on an
    ///     already-broken path (e.g. a duplicate key reappearing on each reconcile) pays nothing to build
    ///     its message after the first report. The dedup key is recorded only once the event is delivered,
    ///     so a diagnostic raised while the sink is momentarily <c>null</c> is not permanently suppressed.
    ///     Callers should namespace <paramref name="dedupKey" /> (e.g. <c>"dupkey:" + key</c>) so unrelated
    ///     call sites can't collide in the shared dedup set.
    /// </summary>
    public static void ReportOnce(
        string dedupKey,
        RaskLogLevel level,
        string category,
        Func<string> messageFactory,
        Exception? exception = null)
    {
        // Nothing to deliver to, so don't burn the dedup key — a later, real sink should still see it.
        var sink = Sink;
        if (sink is null)
        {
            return;
        }

        lock (ReportedOnce)
        {
            if (ReportedOnce.Count >= ReportOnceCap || !ReportedOnce.Add(dedupKey))
            {
                return;
            }
        }

        sink.Invoke(new RaskDiagnosticEvent(level, category, messageFactory(), exception));
    }

    private static void WriteToStandardError(RaskDiagnosticEvent e) =>
        Console.Error.WriteLine(FormatDefault(e));

    // The single-line stderr rendering used by the default sink. Extracted so it can be unit-tested
    // without redirecting the process-global Console.Error stream.
    internal static string FormatDefault(RaskDiagnosticEvent e) =>
        e.Exception is null ? e.Message : $"{e.Message}: {e.Exception}";

    // Test hook: clears the ReportOnce dedup set so a test can exercise the once-per-key behaviour
    // deterministically regardless of what earlier tests reported.
    internal static void ResetReportOnceForTests()
    {
        lock (ReportedOnce)
        {
            ReportedOnce.Clear();
        }
    }
}
