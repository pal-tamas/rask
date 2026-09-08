namespace Rask.Ui;

/// <summary>
/// One number in an <see cref="UiMetricRow" />, optionally the control that selects it.
/// </summary>
/// <remarks>
/// Giving a tile an <see cref="Href" /> turns the row into a filter: the reference shows metrics above a
/// list and repeats the same counts in the tabs that filter it, and printing five numbers twice on one
/// screen is worse than either. A tile that filters is a real link with a real URL, so the selection stays
/// shareable and reachable by keyboard.
/// </remarks>
public sealed partial class UiMetric : Component
{
    public required string Label { get; set; }

    public required string Value { get; set; }

    /// <summary>
    /// <see cref="UiTone.Error" /> for a number someone must act on, <see cref="UiTone.Warning" /> for one
    /// that is merely unproven. Anything else reads as neutral.
    /// </summary>
    public UiTone? Tone { get; set; }

    public string? Caption { get; set; }

    /// <summary>Makes the tile the control that selects this slice.</summary>
    public string? Href { get; set; }

    /// <summary>Whether this tile's slice is the one being shown. Only meaningful with <see cref="Href" />.</summary>
    public bool? Active { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            UiTone.Error => "text-error",
            UiTone.Warning => "text-warning",
            _ => "text-base-content",
        };

        Component body =
            // Stacked on a phone, label-and-value on one line from sm up. Side by side inside a 150px cell
            // truncates one or the other, and the label is the half that stops making sense truncated.
            Div.Class("flex flex-col gap-0.5 sm:flex-row sm:items-baseline sm:justify-between sm:gap-2")[
                Span.Class("truncate text-xs font-medium opacity-60")[Label],
                // Tabular figures so a polling value does not jitter its neighbours as digits change.
                Span.Class($"text-xl font-semibold tabular-nums tracking-tight sm:text-2xl {tone}")[Value]
            ];

        Component? caption = Caption is null ? null : Div.Class("mt-1 truncate text-xs opacity-60")[Caption];

        if (Href is not { } href)
        {
            return Div.Class("bg-base-100 p-3 sm:p-4")[body, caption];
        }

        var selected = Active == true;

        var tile = NavLink
            .Href(href)
            // An inset bottom bar, echoing the section tabs' underline — not a ring and not a border. A
            // border moves the tile's content by a pixel as the selection changes, and a ring is drawn at
            // the cell's bounds, which sit flush against the row's own border and clipped edge: it rendered
            // as a box slightly out of register with the tile it was meant to mark.
            .Class("block p-3 no-underline transition-colors sm:p-4 " + (selected
                ? "bg-base-200 shadow-[inset_0_-2px_0_0_var(--color-ui-ink)]"
                : "bg-base-100 hover:bg-base-200"));

        // Only when true — see UiNavTab. A ternary here would ship a meaningless attribute on every
        // unselected tile.
        if (selected)
        {
            tile = tile.Attributes(("aria-current", "page"));
        }

        return tile[body, caption];
    }
}
