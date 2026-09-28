namespace Rask.DevTools.Probe;

/// <summary>One error, as the Errors tab lists it.</summary>
/// <param name="Sequence">Monotonic per log; a repeat keeps its first number.</param>
/// <param name="At">When it last happened, on the machine running the app.</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="IsWarning">A framework warning rather than an error.</param>
/// <param name="Title">The exception's type, or the diagnostic's category.</param>
/// <param name="Message">What went wrong, in one line or a few.</param>
/// <param name="Detail">The stack, or the exception as .NET writes it, bounded; null when there is none.</param>
/// <param name="Path">The components it happened in, outermost first; empty when no component is known.</param>
/// <param name="ComponentId">The Tree tab's id for the innermost component, when there is one.</param>
/// <param name="Caught">An error boundary took it.</param>
/// <param name="AppWide">Reported outside any page's render or handler, so it belongs to no one page.</param>
/// <param name="Count">How many times it happened in a row.</param>
/// <param name="LikelyFrameworkBug">Its stack points at Rask rather than the app, so it may be reported as a framework bug.</param>
/// <param name="ReportFrames">The frames a report may carry; see <see cref="DevToolsBugReport" />.</param>
internal sealed record DevToolsError(
    long Sequence,
    DateTimeOffset At,
    DevToolsErrorKind Kind,
    bool IsWarning,
    string Title,
    string Message,
    string? Detail,
    IReadOnlyList<string> Path,
    long? ComponentId,
    bool Caught,
    bool AppWide,
    int Count,
    bool LikelyFrameworkBug = false,
    IReadOnlyList<string>? ReportFrames = null)
{
    /// <summary>The frames a report may carry, never null.</summary>
    public IReadOnlyList<string> ReportFrames { get; init; } = ReportFrames ?? [];
}
