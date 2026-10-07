namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline.item</c>: one event of a <see cref="UiTimeline" />, with the line leading into
///     it and the line leading out.
/// </summary>
/// <remarks>
///     It holds a <see cref="UiTimelineIndicator" /> and a <see cref="UiTimelineContent" />, or a single
///     <see cref="UiTimelineBlock" />. The first item's leading line and the last one's trailing line are
///     there and not drawn, so every item is the same shape.
/// </remarks>
public sealed partial class UiTimelineItem : Component
{
    /// <summary>How far along this item is: it draws the indicator, and a complete item darkens the line on to the next.</summary>
    public Ui.TimelineStatus? Status { get; set; }

    /// <summary>Where this item's content sits beside its indicator, when not where the timeline puts it.</summary>
    public Ui.TimelineAlign? Align { get; set; }

    /// <summary>The size of this item's indicator, when not the timeline's.</summary>
    public Ui.TimelineSize? Size { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var status = Status ?? Ui.TimelineStatus.Default;
        var size = Size ?? Context.Get<UiTimelineScope>()?.Size ?? Ui.TimelineSize.Base;
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-timeline-item"] = null };
        if (UiTimelineNames.Of(status) is { } named)
        {
            marks["ui-timeline-status"] = named;
        }

        if (Align is { } align)
        {
            marks["ui-timeline-align"] = UiTimelineNames.Of(align);
        }

        if (Size == Ui.TimelineSize.Lg)
        {
            marks["ui-timeline-size"] = "lg";
        }

        return Li.Data(marks).Class(Class)[
            Div.Data("ui-timeline-line-leading")[Div],
            Div.Data("ui-timeline-gap-leading"),
            Context.Provide(new UiTimelineScope(size, status))[Children ?? []],
            Div.Data("ui-timeline-line-trailing")[Div],
            Div.Data("ui-timeline-gap-trailing")
        ];
    }
}
