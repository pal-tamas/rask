namespace Rask;

/// <summary>
///     What a timeline and an item tell the indicator inside them: the size and the status it is drawn in.
/// </summary>
internal sealed record UiTimelineScope(Ui.TimelineSize Size, Ui.TimelineStatus Status);
