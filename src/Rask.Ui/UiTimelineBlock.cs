namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline.block</c>: a card, a callout or any block as a <see cref="UiTimelineItem" />,
///     spanning the timeline's whole width with no indicator of its own.
/// </summary>
/// <remarks>
///     The line runs into it from above and out below. Its children span the width too, except a
///     <see cref="UiTimelineSubgrid" />, which puts them back on the timeline's columns.
/// </remarks>
public sealed partial class UiTimelineBlock : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-timeline-block");

    /// <inheritdoc />
    protected override string TagName => "div";

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
