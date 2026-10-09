namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline.content</c>: what a <see cref="UiTimelineItem" /> says, beside its indicator.
/// </summary>
/// <remarks>
///     A <c>&lt;div&gt;</c> the timeline places: beside the indicator down the page, under it across. In an
///     incomplete item it is dimmed.
/// </remarks>
public sealed partial class UiTimelineContent : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-timeline-content");

    /// <inheritdoc />
    protected override string TagName => "div";

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
