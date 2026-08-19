using Rask.Core.Diagnostics;

namespace Rask.Testing;

/// <summary>
///     Captures the framework diagnostics raised while it is installed, so a test can assert that
///     something was reported — or that nothing was.
/// </summary>
/// <remarks>
///     <para>
///         Swallow-and-log is the framework's primary failure mode for the faults it must not let escape:
///         a navigate that threw, a JS dispatch that faulted, a malformed frame, a lifecycle hook with no
///         boundary above it. Those go to <c>RaskDiagnostics</c>, which is <c>internal</c>, so an app
///         author had no supported way to assert on any of them — the fault was reported to stderr and
///         the test saw nothing.
///     </para>
///     <para>
///         This is a public wrapper rather than a public <c>RaskDiagnostics</c>: <c>Rask.Testing</c> is on
///         <c>Rask.Core</c>'s <c>InternalsVisibleTo</c> list, so the capability needs no new public seam
///         on the framework — and a public seam is an irreversible commitment where this is not.
///     </para>
///     <para>
///         <b>The sink is process-global</b>, so installs are tracked as a set rather than as a single
///         slot: every capture that is currently installed receives every event, and the sink that was
///         there before is restored when the last one is disposed. That is what makes this safe under
///         xUnit's default parallelism — with a save/restore slot, two test classes installing at once
///         meant the first to dispose silently unhooked the second, which then captured nothing and
///         failed on a full-solution run while passing standalone (#769).
///     </para>
///     <para>
///         Concurrent captures therefore see each other's events. A test asserting on a <em>count</em>
///         should filter to the events it provoked (<see cref="OfCategory" />) rather than assert over
///         everything captured.
///     </para>
///     <code>
///     using var diagnostics = CapturingDiagnostics.Install();
///     await page.ClickAsync("#save");
///     Assert.Empty(diagnostics.Errors);
///     </code>
/// </remarks>
public sealed class CapturingDiagnostics : IDisposable
{
    private static readonly Lock InstallGate = new();

    // Every capture currently installed. Guarded by InstallGate for mutation; readers take a snapshot
    // under the same lock, because a diagnostic can be reported from any thread at any time.
    private static readonly List<CapturingDiagnostics> Installed = [];

    // The sink that was in place before the FIRST capture installed — restored when the last one goes.
    private static Action<RaskDiagnosticEvent>? _outerSink;

    private readonly Lock _gate = new();
    private readonly List<CapturedDiagnostic> _captured = [];
    private bool _disposed;

    private CapturingDiagnostics()
    {
    }

    /// <summary>
    ///     Installs a capturing sink. Dispose to remove it; the sink that was in place before the first
    ///     install is restored once the last capture is disposed, in whatever order they are disposed.
    /// </summary>
    public static CapturingDiagnostics Install()
    {
        var capture = new CapturingDiagnostics();

        lock (InstallGate)
        {
            if (Installed.Count == 0)
            {
                _outerSink = RaskDiagnostics.Sink;
                RaskDiagnostics.Sink = Fanout;
            }

            Installed.Add(capture);
            RaskDiagnostics.ResetReportOnceForTests();
        }

        return capture;
    }

    // One sink, fanned out to every installed capture. Snapshotting under the lock keeps a Dispose that
    // races a report from mutating the list mid-iteration; the captures' own Capture takes its own lock.
    private static void Fanout(RaskDiagnosticEvent e)
    {
        CapturingDiagnostics[] targets;
        lock (InstallGate)
        {
            targets = Installed.ToArray();
        }

        foreach (var target in targets)
        {
            target.Capture(e);
        }
    }

    /// <summary>Everything captured so far, oldest first.</summary>
    public IReadOnlyList<CapturedDiagnostic> Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured.ToArray();
            }
        }
    }

    /// <summary>Captured events at error level — the ones that mean something was swallowed.</summary>
    public IReadOnlyList<CapturedDiagnostic> Errors =>
        Captured.Where(e => e.Level == DiagnosticLevel.Error).ToArray();

    /// <summary>Captured events from one subsystem, e.g. <c>"Rask.Lifecycle"</c> or <c>"Rask.JsInvoke"</c>.</summary>
    public IReadOnlyList<CapturedDiagnostic> OfCategory(string category) =>
        Captured.Where(e => string.Equals(e.Category, category, StringComparison.Ordinal)).ToArray();

    /// <summary>
    ///     Removes this capture. The sink that was in place before the first install is restored once the
    ///     last capture is disposed — a capture still in use by another test is never unhooked.
    /// </summary>
    public void Dispose()
    {
        lock (InstallGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Installed.Remove(this);

            if (Installed.Count == 0)
            {
                RaskDiagnostics.Sink = _outerSink;
                _outerSink = null;
            }

            RaskDiagnostics.ResetReportOnceForTests();
        }
    }

    private void Capture(RaskDiagnosticEvent e)
    {
        lock (_gate)
        {
            _captured.Add(new CapturedDiagnostic(
                (DiagnosticLevel)e.Level, e.Category, e.Message, e.Exception));
        }
    }
}

/// <summary>Severity of a captured framework diagnostic.</summary>
public enum DiagnosticLevel
{
    /// <summary>Something worth knowing that is not a fault.</summary>
    Information,

    /// <summary>A degradation the app survives — a budget exceeded, a feature falling back.</summary>
    Warning,

    /// <summary>A fault the framework caught and did not let escape.</summary>
    Error,
}

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
