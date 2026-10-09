namespace Rask;

/// <summary>
///     The words a timeline's options are written in on its <c>data-ui-timeline-*</c> attributes, which are
///     what the stylesheet reads.
/// </summary>
internal static class UiTimelineNames
{
    internal static string Of(Ui.TimelineAlign align) => align switch
    {
        Ui.TimelineAlign.Start => "start",
        Ui.TimelineAlign.Baseline => "baseline",
        Ui.TimelineAlign.End => "end",
        _ => "center",
    };

    /// <summary>Null for no status, which writes no attribute.</summary>
    internal static string? Of(Ui.TimelineStatus status) => status switch
    {
        Ui.TimelineStatus.Complete => "complete",
        Ui.TimelineStatus.Current => "current",
        Ui.TimelineStatus.Incomplete => "incomplete",
        _ => null,
    };
}
