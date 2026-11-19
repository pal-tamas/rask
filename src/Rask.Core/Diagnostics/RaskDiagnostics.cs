namespace Rask.Core.Diagnostics;

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
    // The process's own record; a capture a test installs (Rask.Testing's CapturingDiagnostics) brings one of its own
    // for the flow it runs on (BeginReportOnceScope), so tests in parallel neither silence nor repeat each other.
    private static readonly ReportedOnce Global = new();
    private static readonly AsyncLocal<ReportedOnce?> Scoped = new();

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
        var diagnostic = new RaskDiagnosticEvent(level, category, message, exception);
        DevTools.RaskDevToolsHook.Active?.DiagnosticReported(diagnostic);
        sink?.Invoke(diagnostic);
    }

    /// <summary>
    ///     Surface a diagnostic at most once per distinct <paramref name="dedupKey" />, bounded to
    ///     the last <see cref="ReportedOnce.Cap" /> distinct keys. <paramref name="messageFactory" /> is invoked only
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

        if (!(Scoped.Value ?? Global).Add(dedupKey))
        {
            return;
        }

        var diagnostic = new RaskDiagnosticEvent(level, category, messageFactory(), exception);
        DevTools.RaskDevToolsHook.Active?.DiagnosticReported(diagnostic);
        sink.Invoke(diagnostic);
    }

    private static void WriteToStandardError(RaskDiagnosticEvent e) =>
        Console.Error.WriteLine(FormatDefault(e));

    // The single-line stderr rendering used by the default sink. Extracted so it can be unit-tested
    // without redirecting the process-global Console.Error stream.
    //
    // Carries the level and the category, which the event has always had and this always dropped. A host
    // with a bridge installed never sees this — but a host WITHOUT one (WASM until now, and any app that
    // hasn't wired logging) got a bare sentence with no severity and no subsystem, so
    // "framework said something" and "framework reported an error in the diff codec" read identically.
    // Fixing it here fixes it for every unbridged host at once, which a per-host bridge cannot.
    internal static string FormatDefault(RaskDiagnosticEvent e)
    {
        var head = $"[Rask:{e.Category}] {Label(e.Level)}: {e.Message}";
        return e.Exception is null ? head : $"{head}: {e.Exception}";
    }

    private static string Label(RaskLogLevel level) => level switch
    {
        RaskLogLevel.Error => "error",
        RaskLogLevel.Warning => "warning",
        _ => "info"
    };

    // Test hook: clears the ReportOnce dedup set so a test can exercise the once-per-key behaviour
    // deterministically regardless of what earlier tests reported.
    internal static void ResetReportOnceForTests()
    {
        (Scoped.Value ?? Global).Clear();
    }

    /// <summary>
    ///     Gives the calling flow — and what it awaits or starts from here — a ReportOnce record of its own, empty, until
    ///     the returned scope is disposed. What a test provokes is then reported to it once, whatever another test
    ///     running beside it reported, and resetting it resets nobody else's.
    /// </summary>
    internal static ReportOnceScope BeginReportOnceScope()
    {
        var before = Scoped.Value;
        Scoped.Value = new ReportedOnce();
        return new ReportOnceScope(before);
    }

    /// <summary>Puts back the record the flow had before <see cref="BeginReportOnceScope" />.</summary>
    internal readonly struct ReportOnceScope(ReportedOnce? before) : IDisposable
    {
        public void Dispose() => Scoped.Value = before;
    }
}
