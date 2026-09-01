namespace Rask.Dashboard.Pages;

/// <summary>
/// A button, in one of the console's four weights.
/// </summary>
/// <remarks>
/// <para>
/// Both <see cref="OnClick" /> and <see cref="OnClickAsync" /> exist because both call sites exist: an
/// action that awaits a panel, and a state flip that does not. Making every caller wrap a void in a
/// completed task would be noise at the call site to save one property here.
/// </para>
/// <para>
/// <c>min-h-11</c> below <c>sm</c> is not decoration: 44px is the smallest reliable touch target, and the
/// console's buttons are <c>text-xs</c> — at a desk that reads as precise, on a phone it reads as a
/// mis-tap. The height relaxes from <c>sm</c> up where there is a pointer.
/// </para>
/// </remarks>
internal sealed partial class OpsButton : Component
{
    public required string Label { get; set; }

    /// <summary>
    /// One of <c>primary</c>, <c>danger</c>, <c>quiet</c>. Anything else is the ordinary bordered button.
    /// </summary>
    public string? Tone { get; set; }

    public OpsIconName? Icon { get; set; }

    public Action? OnClick { get; set; }

    public Func<Task>? OnClickAsync { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    private const string Base =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg px-3 text-xs font-medium "
        + "transition-colors disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-ops-brand sm:min-h-0 sm:py-1.5";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var button = Button
            .Type("button")
            .Class($"{Base} {Palette()} {Class}")
            .Disabled(Disabled == true);

        // Whichever the caller supplied. Both set would be a call-site bug, and the async one wins because
        // it is the one that does work.
        if (OnClickAsync is { } async)
        {
            button = button.OnClickAsync(async);
        }
        else if (OnClick is { } sync)
        {
            button = button.OnClick(sync);
        }

        return button[
            Icon is { } icon ? OpsIcon.Name(icon).Class("size-4 shrink-0") : null,
            Span[Label]
        ];
    }

    private string Palette() => Tone switch
    {
        "primary" => "bg-ops-ink text-ops-bg hover:bg-ops-ink/90",
        "danger" => "border border-ops-line bg-ops-bg text-ops-danger hover:border-ops-danger/40 hover:bg-ops-danger/5",
        "quiet" => "text-ops-muted hover:bg-ops-well hover:text-ops-ink",
        _ => "border border-ops-line bg-ops-bg text-ops-ink hover:bg-ops-well",
    };
}

/// <summary>
/// The console's search field: a leading icon, and the filter it drives.
/// </summary>
/// <remarks>
/// Deliberately a page-level control rather than one in the top bar. The reference puts a global
/// <c>⌘K</c> search in its chrome; this console has nothing global to search, and a box that greets an
/// operator with no results for everything they type is worse than no box. So it lives on the three pages
/// that really do filter — logs, cache and a queue's dead letters — and searches what it sits above.
/// </remarks>
internal sealed partial class OpsSearch : Component
{
    public required string Placeholder { get; set; }

    /// <summary>The accessible name. The placeholder is not one — it disappears exactly when typing starts.</summary>
    public required string Label { get; set; }

    public string? Value { get; set; }

    public Func<string, Task>? OnSearch { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var input = Input
            .Value(Value ?? string.Empty)
            .Type(InputType.Search)
            .Placeholder(Placeholder)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(
                "min-h-11 w-full rounded-lg border border-ops-line bg-ops-bg py-1.5 pl-9 pr-3 text-sm text-ops-ink "
                + "placeholder:text-ops-muted focus-visible:outline-2 focus-visible:outline-offset-2 "
                + "focus-visible:outline-ops-brand sm:min-h-0");

        if (OnSearch is { } search)
        {
            input = input.OnChangeAsync(search);
        }

        // Full width on a phone, a sane column from sm up: on a 360px screen a fixed-width search box either
        // overflows the row or leaves the rest of it stranded.
        return Div.Class($"relative w-full sm:w-72 {Class}")[
            OpsIcon.Name(OpsIconName.Search)
                .Class("pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ops-muted"),
            input
        ];
    }
}

/// <summary>
/// A filled dot and what it means — the console's quietest way to say a state.
/// </summary>
/// <remarks>
/// The label is required rather than optional. Colour alone is not a status: an operator who cannot
/// distinguish the teal from the amber would otherwise be reading an unlabelled dot.
/// </remarks>
internal sealed partial class OpsStatusDot : Component
{
    public required string Label { get; set; }

    /// <summary>One of <c>ok</c>, <c>warn</c>, <c>danger</c>, <c>busy</c>. Anything else reads as idle.</summary>
    public string? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("inline-flex items-center gap-1.5 whitespace-nowrap text-xs text-ops-muted")[
            Span.Class($"size-1.5 shrink-0 rounded-full {Palette()}").Attributes(("aria-hidden", "true")),
            Span[Label]
        ];

    private string Palette() => Tone switch
    {
        "ok" => "bg-ops-ok",
        "warn" => "bg-ops-warn",
        "danger" => "bg-ops-danger",
        "busy" => "bg-ops-brand",
        _ => "bg-ops-line",
    };
}
