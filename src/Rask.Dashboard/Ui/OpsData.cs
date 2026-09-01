namespace Rask.Dashboard.Pages;

/// <summary>
/// The row of headline numbers at the top of a page.
/// </summary>
/// <remarks>
/// <para>
/// Two columns on a phone, four from <c>sm</c> up — so the reference's four-across row survives contact
/// with a 360px screen instead of squeezing four numbers into 80px each.
/// </para>
/// <para>
/// The hairlines are a <c>gap-px</c> over a lined background rather than borders on the cells. Cell borders
/// have to know how many siblings they have and which edge is last, and get it wrong the moment the number
/// of registered batteries changes; a lined background is correct for any count at any breakpoint.
/// </para>
/// </remarks>
internal sealed partial class OpsMetricRow : Component
{
    /// <summary>How many across from <c>sm</c> up. Four unless said otherwise; two on a phone regardless.</summary>
    /// <remarks>
    /// Spelled as whole literal class strings rather than composed from the number. Tailwind scans this file
    /// for class names as TEXT — an interpolated <c>sm:grid-cols-{n}</c> is not a class name it can see, and
    /// the utility would simply never be emitted, with an unstyled grid as the only symptom.
    /// </remarks>
    public int? Columns { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(
            "grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-ops-line bg-ops-line " + (Columns switch
            {
                // An odd number of tiles in two columns leaves the last slot empty, and because the
                // hairlines are the container's own background showing through the gaps, an empty slot is
                // not blank — it is a grey block that reads as a broken tile. The last one spans the row
                // instead, and goes back to a single column once there are enough columns to divide evenly.
                3 => "sm:grid-cols-3 [&>*:last-child]:col-span-2 sm:[&>*:last-child]:col-span-1",
                5 => "sm:grid-cols-5 [&>*:last-child]:col-span-2 sm:[&>*:last-child]:col-span-1",
                _ => "sm:grid-cols-4",
            }))[
            Children ?? []
        ];
}

/// <summary>
/// One number in an <see cref="OpsMetricRow" />, optionally the control that selects it.
/// </summary>
/// <remarks>
/// Giving a tile an <see cref="Href" /> turns the row into a filter: the reference shows metrics above a
/// list and repeats the same counts in the tabs that filter it, and printing five numbers twice on one
/// screen is worse than either. A tile that filters is a real link with a real URL, so the selection stays
/// shareable and reachable by keyboard.
/// </remarks>
internal sealed partial class OpsMetric : Component
{
    public required string Label { get; set; }

    public required string Value { get; set; }

    /// <summary>
    /// <c>danger</c> for a number an operator must act on, <c>warn</c> for one that is merely unproven.
    /// Anything else reads as neutral.
    /// </summary>
    public string? Tone { get; set; }

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
            "danger" => "text-ops-danger",
            "warn" => "text-ops-warn-ink",
            _ => "text-ops-ink",
        };

        Component body =
            // Stacked on a phone, label-and-value on one line from sm up. Side by side inside a 150px cell
            // truncates one or the other, and the label is the half that stops making sense truncated.
            Div.Class("flex flex-col gap-0.5 sm:flex-row sm:items-baseline sm:justify-between sm:gap-2")[
                Span.Class("truncate text-xs font-medium text-ops-muted")[Label],
                // Tabular figures so a polling value does not jitter its neighbours as digits change.
                Span.Class($"text-xl font-semibold tabular-nums tracking-tight sm:text-2xl {tone}")[Value]
            ];

        Component? caption = Caption is null ? null : Div.Class("mt-1 truncate text-xs text-ops-muted")[Caption];

        if (Href is not { } href)
        {
            return Div.Class("bg-ops-panel p-3 sm:p-4")[body, caption];
        }

        var selected = Active == true;

        var tile = NavLink
            .Href(href)
            // An inset bottom bar, echoing the section tabs' underline — not a ring and not a border. A
            // border moves the tile's content by a pixel as the selection changes, and a ring is drawn at
            // the cell's bounds, which sit flush against the row's own border and clipped edge: it rendered
            // as a box slightly out of register with the tile it was meant to mark.
            .Class("block p-3 no-underline transition-colors sm:p-4 " + (selected
                ? "bg-ops-well shadow-[inset_0_-2px_0_0_var(--color-ops-ink)]"
                : "bg-ops-panel hover:bg-ops-well"));

        // Only when true — see OpsNavTab. A ternary here would ship a meaningless attribute on every
        // unselected tile.
        if (selected)
        {
            tile = tile.Attributes(("aria-current", "page"));
        }

        return tile[body, caption];
    }
}

/// <summary>The key-and-value list a detail sheet is made of.</summary>
internal sealed partial class OpsDetailList : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("divide-y divide-ops-line")[Children ?? []];
}

/// <summary>
/// One key and its value, joined by a dotted leader.
/// </summary>
/// <remarks>
/// The leader is the reference's most recognisable detail, and it is also the thing that cannot survive a
/// narrow screen: a dashed rule between "Queued time" and a timestamp has nowhere to go at 360px. So below
/// <c>sm</c> the pair stacks and the leader is hidden outright, which is what it would have degenerated
/// into anyway.
/// </remarks>
internal sealed partial class OpsDetailRow : Component
{
    public required string Label { get; set; }

    public required string Value { get; set; }

    /// <summary>Monospace, for ids, sizes and durations — anything a machine produced.</summary>
    public bool? Mono { get; set; }

    /// <summary>
    /// <c>danger</c> or <c>warn</c> to colour the value. Anything else reads as neutral.
    /// </summary>
    public string? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            "danger" => "text-ops-danger",
            "warn" => "text-ops-warn-ink",
            _ => "text-ops-ink",
        };

        return Div.Class("flex flex-col gap-0.5 py-2.5 sm:flex-row sm:items-baseline sm:gap-3")[
            Span.Class("shrink-0 text-sm text-ops-muted")[Label],
            Span.Class("hidden min-w-4 grow translate-y-[-0.2rem] border-b border-dashed border-ops-line sm:block")
                .Attributes(("aria-hidden", "true")),
            Span.Class((Mono == true
                ? "break-all font-mono text-xs sm:shrink-0 sm:text-right "
                : "break-words text-sm sm:shrink-0 sm:text-right ") + tone)[Value]
        ];
    }
}

/// <summary>
/// A block of machine output — a stack trace, a stored payload.
/// </summary>
/// <remarks>
/// Scrolls on its own and wraps on overflow, because the alternative is a 400-character exception message
/// making the whole page scroll sideways. <c>danger</c> for anything that is the reason a job is dead.
/// </remarks>
internal sealed partial class OpsCode : Component
{
    // Not `Text`: that name is the Text component's builder entry, inherited from Component (CS0108).
    public required string Content { get; set; }

    /// <summary><c>danger</c> to read it as a failure. Anything else is neutral machine output.</summary>
    public string? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Pre.Class(
            "max-h-72 overflow-auto whitespace-pre-wrap break-all rounded-lg border border-ops-line bg-ops-well "
            + "p-3 font-mono text-xs " + (Tone == "danger" ? "text-ops-danger" : "text-ops-muted"))[
            Content
        ];
}
