namespace Rask;

/// <summary>
///     Flux's <c>flux:timeline</c>: events or steps in order, down the page or across it.
/// </summary>
/// <remarks>
///     <para>
///     An <c>&lt;ol&gt;</c> of <see cref="UiTimelineItem" />s, each an indicator on the line and the content
///     beside it:
///     </para>
///     <code>
///     Ui.Timeline[
///         Ui.TimelineItem[
///             Ui.TimelineIndicator.Color(Ui.Color.Green)[Ui.Icon.Name(Ui.IconName.Check).Micro],
///             Ui.TimelineContent["Approved"]
///         ]
///     ]
///     </code>
///     <para>
///     The space between items and between indicator and content are two CSS variables,
///     <c>--ui-timeline-item-gap</c> and <c>--ui-timeline-content-gap</c>, set from <see cref="Class" />.
///     </para>
/// </remarks>
public sealed partial class UiTimeline : Component
{
    /// <summary>Across the page rather than down it.</summary>
    public bool? Horizontal { get; set; }

    /// <summary>Where every item's content sits beside its indicator. Centre when unset.</summary>
    public Ui.TimelineAlign? Align { get; set; }

    /// <summary>The size of the indicators, and with it the line and the gaps.</summary>
    public Ui.TimelineSize? Size { get; set; }

    /// <summary>Classes for the call site, added to the timeline's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var size = Size ?? Ui.TimelineSize.Base;
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-timeline"] = null,
            ["ui-timeline-align"] = UiTimelineNames.Of(Align ?? Ui.TimelineAlign.Center),
        };

        if (size == Ui.TimelineSize.Lg)
        {
            marks["ui-timeline-size"] = "lg";
        }

        if (Horizontal is true)
        {
            marks["ui-timeline-horizontal"] = null;
        }

        return Ol.Data(marks).Class(Class)[
            Context.Provide(new UiTimelineScope(size, Ui.TimelineStatus.Default))[Children ?? []]
        ];
    }
}
