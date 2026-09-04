namespace Rask.Ui;

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
public sealed partial class UiMetricRow : Component
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
            "grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-base-300 bg-base-300 " + (Columns switch
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

/// <summary>The key-and-value list a detail sheet is made of.</summary>
public sealed partial class UiDetailList : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("divide-y divide-ui-line")[Children ?? []];
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
public sealed partial class UiDetailRow : Component
{
    public required string Label { get; set; }

    public required string Value { get; set; }

    /// <summary>Monospace, for ids, sizes and durations — anything a machine produced.</summary>
    public bool? Mono { get; set; }

    /// <summary>
    /// <see cref="UiTone.Error" /> or <see cref="UiTone.Warning" /> to colour the value. Anything else
    /// reads as neutral.
    /// </summary>
    public UiTone? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            UiTone.Error => "text-error",
            UiTone.Warning => "text-warning",
            _ => "text-base-content",
        };

        return Div.Class("flex flex-col gap-0.5 py-2.5 sm:flex-row sm:items-baseline sm:gap-3")[
            Span.Class("shrink-0 text-sm opacity-60")[Label],
            Span.Class("hidden min-w-4 grow translate-y-[-0.2rem] border-b border-dashed border-base-300 sm:block")
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
/// making the whole page scroll sideways. <see cref="UiTone.Error" /> for anything that is the reason
/// something failed.
/// </remarks>
public sealed partial class UiCode : Component
{
    // Not `Text`: that name is the Text component's builder entry, inherited from Component (CS0108).
    public required string Content { get; set; }

    /// <summary><see cref="UiTone.Error" /> to read it as a failure. Anything else is neutral machine output.</summary>
    public UiTone? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Pre.Class(
            "max-h-72 overflow-auto whitespace-pre-wrap break-all rounded-lg border border-base-300 bg-base-200 "
            + "p-3 font-mono text-xs " + (Tone == UiTone.Error ? "text-error" : "opacity-60"))[
            Content
        ];
}
