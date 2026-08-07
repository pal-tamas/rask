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
///         <b>The sink is process-global</b>, so this serializes on a lock and restores the previous sink
///         on <see cref="Dispose" />. Two of these live at once in parallel tests would still interleave —
///         if a test asserts on a <em>count</em>, filter to the events it provoked (<see cref="OfCategory" />)
///         rather than asserting over everything captured.
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

    private readonly Lock _gate = new();
    private readonly List<CapturedDiagnostic> _captured = [];
    private readonly Action<RaskDiagnosticEvent>? _previous;
    private bool _disposed;

    private CapturingDiagnostics()
    {
        _previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        RaskDiagnostics.Sink = Capture;
    }

    /// <summary>Installs a capturing sink. Dispose to restore the previous one.</summary>
    public static CapturingDiagnostics Install()
    {
        lock (InstallGate)
        {
            return new CapturingDiagnostics();
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

    /// <summary>Restores the sink that was installed before this one.</summary>
    public void Dispose()
    {
        lock (InstallGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RaskDiagnostics.Sink = _previous;
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
