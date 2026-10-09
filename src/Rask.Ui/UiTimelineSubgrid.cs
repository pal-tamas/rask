namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline.subgrid</c>: inside a <see cref="UiTimelineBlock" />, back on the timeline's own
///     columns.
/// </summary>
/// <remarks>
///     Its first child sits in the indicators' column, centred, and its second in the content's — an avatar
///     and a comment, lined up with the events above and below the block.
/// </remarks>
public sealed partial class UiTimelineSubgrid : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-timeline-subgrid");

    /// <inheritdoc />
    protected override string TagName => "div";

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
