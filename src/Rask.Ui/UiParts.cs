using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>
/// The kit's class-name vocabulary, for markup a component does not cover.
/// </summary>
/// <remarks>
/// Constants rather than <c>@apply</c>: <c>@apply</c> moves the decision into a stylesheet Tailwind
/// then has to be told about, while a constant is read by the compiler, renamed by the IDE, and found
/// by Tailwind's scanner like any other literal. Use a component where one exists — these are for the
/// gaps between them.
/// </remarks>
public static class UiStyles
{
    /// <summary>A panel: hairline border, no shadow, and padding that tightens on a phone.</summary>
    public const string Card = "rounded-xl border border-base-300 bg-base-100 p-4 sm:p-5";

    /// <summary>The small muted label above a value.</summary>
    public const string Label = "text-xs font-medium tracking-wide opacity-60";

    /// <summary>A headline number. Tabular figures so a polling value does not jitter as digits change.</summary>
    public const string Value = "mt-2 text-2xl font-semibold tabular-nums tracking-tight text-base-content sm:text-3xl";

    /// <summary>The quiet line under a value.</summary>
    public const string Caption = "mt-1 text-xs opacity-60";

    /// <summary>A page's own heading.</summary>
    public const string Heading = "text-base font-semibold tracking-tight text-base-content sm:text-lg";

    // min-h-11 below sm on every control: 44px is the smallest reliable touch target, and these are all
    // text-xs. The height relaxes from sm up, where there is a pointer.

    /// <summary>A secondary action.</summary>
    public const string Button =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-base-300 "
        + "bg-base-100 px-3 text-xs font-medium text-base-content transition-colors hover:bg-base-200 "
        + "disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-primary sm:min-h-0 sm:py-1.5";

    /// <summary>An action that destroys or re-runs work — the only colour on the console.</summary>
    public const string Danger =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-base-300 "
        + "bg-base-100 px-3 text-xs font-medium text-error transition-colors hover:border-ui-danger/40 "
        + "hover:bg-error/5 disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-primary sm:min-h-0 sm:py-1.5";

    /// <summary>A borderless control: a dismiss, a close.</summary>
    public const string Quiet =
        "inline-flex min-h-11 items-center rounded-lg px-2 text-xs opacity-60 hover:bg-base-200 "
        + "hover:text-base-content sm:min-h-0 sm:py-1";

    /// <summary>Monospace, for ids and payloads.</summary>
    public const string Mono = "font-mono text-xs";
}

/// <summary>A bordered panel with an optional heading and an optional action in its corner.</summary>
public sealed partial class UiCard : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public string? Heading { get; set; }

    public Component? Action { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component? header = Heading is null && Action is null
            ? null
            // Wraps rather than truncating: a card's action is often a button whose label is the only thing
            // saying what it does, and on a phone the heading and the action rarely fit on one line.
            : Div.Class("mb-4 flex flex-wrap items-center justify-between gap-3")[
                Heading is null ? null : H2.Class(UiStyles.Heading)[Heading],
                Action
            ];

        return Div.Class($"{UiStyles.Card} {Class}")[header, Children ?? []];
    }
}

/// <summary>One number, with what it counts and where to go for the detail behind it.</summary>
public sealed partial class UiStat : Component
{
    public required string Value { get; set; }

    public required string Label { get; set; }

    // NULLABLE, not defaulted. A property with an initialiser is excluded from the chain altogether, and
    // one that is non-nullable without an initialiser becomes a REQUIRED step (RASK001) — so an optional
    // step is spelled by making the property nullable, and only that.
    public UiIconName? Icon { get; set; }

    public string? Caption { get; set; }

    /// <summary>
    ///     <c>danger</c> for a number an operator must act on, <c>warn</c> for one that is merely unproven.
    ///     Anything else reads as neutral.
    /// </summary>
    /// <remarks>
    ///     Two levels rather than one boolean, and the distinction is the point: a tile that goes red every
    ///     time a check races replication is a tile operators learn to ignore, so "we could not prove this"
    ///     has to look different from "this is broken".
    /// </remarks>
    public string? Tone { get; set; }

    public RouteUrl? Href { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            "danger" => "text-error",
            "warn" => "text-warning",
            _ => null,
        };

        Component body = Div.Class("flex items-start justify-between gap-3")[
            Div.Class("min-w-0")[
                Div.Class($"truncate {UiStyles.Label}")[Label],
                Div.Class(tone is null ? UiStyles.Value : $"{UiStyles.Value} {tone}")[Value],
                Caption is null ? null : Div.Class(UiStyles.Caption)[Caption]
            ],
            UiIcon.Name(Icon ?? UiIconName.Overview)
                .Class($"size-5 shrink-0 {tone ?? "opacity-60"}")
        ];

        // A tile that leads somewhere is a link, so it is reachable by keyboard and says where it goes —
        // rather than a div with a click handler, which is neither.
        return Href is { } href
            ? NavLink.Href(href).Class($"{UiStyles.Card} block no-underline transition-colors hover:bg-base-200")[body]
            : Div.Class(UiStyles.Card)[body];
    }
}

/// <summary>A page heading with its caption and an optional row of controls.</summary>
public sealed partial class UiHeader : Component
{
    public required string Heading { get; set; }

    public string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <summary>Shown before the heading, so a queue page is recognisable at a glance.</summary>
    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 sm:mb-5")[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0 opacity-60") : null,
            H1.Class(UiStyles.Heading)[Heading],
            Caption is null ? null : Span.Class("text-xs opacity-60")[Caption],
            // Full width on its own line below sm, so a row of actions never squeezes the heading to
            // nothing; trailing-aligned beside it from sm up.
            Actions is null ? null : Div.Class("flex w-full flex-wrap gap-2 sm:ml-auto sm:w-auto")[Actions]
        ];
}

/// <summary>A row of link-shaped tabs. Navigation, so each one is a real link with a real URL.</summary>
/// <remarks>
/// Scrolls rather than wraps: these carry counts that change as a queue drains, and a wrapping row would
/// change height underneath an operator mid-read.
/// </remarks>
public sealed partial class UiTabs : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(
            "-mx-3 flex items-center gap-1 overflow-x-auto px-3 sm:mx-0 sm:flex-wrap sm:px-0 "
            + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden")[
            Children ?? []
        ];
}

/// <summary>One tab.</summary>
public sealed partial class UiTab : Component
{
    public required string Href { get; set; }

    public required string Label { get; set; }

    public bool? Active { get; set; }

    /// <summary>A count shown beside the label, for a tab that filters a list.</summary>
    public string? Count { get; set; }

    /// <summary>Colours the count when it is a number worth acting on.</summary>
    public bool? Alarm { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        NavLink
            .Href(Href)
            .Class(Active == true
                ? "flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-lg border border-base-300 "
                  + "bg-base-200 px-3 text-sm font-medium text-base-content no-underline sm:min-h-0 sm:py-1.5"
                : "flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-lg border border-transparent "
                  + "px-3 text-sm opacity-60 no-underline hover:bg-base-200 hover:text-base-content sm:min-h-0 "
                  + "sm:py-1.5")[
            Span[Label],
            Count is null
                ? null
                : Span.Class(Alarm == true
                    ? "rounded bg-error/10 px-1.5 py-0.5 text-xs tabular-nums text-error"
                    : "rounded bg-base-200 px-1.5 py-0.5 text-xs tabular-nums opacity-60")[Count]
        ];
}

/// <summary>An inline notice: a confirmation to answer, or the result of an action just taken.</summary>
public sealed partial class UiNotice : Component
{
    /// <summary>One of <c>danger</c>, <c>warn</c>, <c>info</c>. Anything else reads as neutral.</summary>
    public string? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Role("alert")
            .Class($"mb-4 flex flex-wrap items-center gap-3 rounded-xl border px-4 py-3 text-sm {Palette()}")[
            Children ?? []
        ];

    private string Palette() => Tone switch
    {
        "danger" => "border-ui-danger/30 bg-error/5 text-error",
        "warn" => "border-ui-warn/40 bg-warning/10 text-base-content",
        "info" => "border-ui-brand/30 bg-primary/5 text-base-content",
        _ => "border-base-300 bg-base-200 opacity-60",
    };
}

/// <summary>A small status pill.</summary>
public sealed partial class UiBadge : Component
{
    public required string Label { get; set; }

    /// <summary>One of <c>danger</c>, <c>warn</c>, <c>info</c>, <c>ok</c>. Anything else reads as neutral.</summary>
    public string? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class(
            $"inline-flex items-center rounded-md px-1.5 py-0.5 text-xs font-medium {Palette()} {Class}")[
            Label
        ];

    private string Palette() => Tone switch
    {
        "danger" => "bg-error/10 text-error",
        "warn" => "bg-warning/15 text-warning",
        "info" => "bg-primary/10 text-primary",
        "ok" => "bg-success/10 text-success",
        _ => "bg-base-200 opacity-60",
    };
}

/// <summary>
/// A table that scrolls on its own rather than making the page scroll sideways.
/// </summary>
/// <remarks>
/// The header row and the cell padding live here so every table on the console matches; a page supplies
/// only its <c>thead</c> and <c>tbody</c>.
/// <para>
/// The horizontal scroll is a backstop, not the mobile plan. A table an operator has to swipe sideways to
/// read has hidden the column they came for, so pages drop their secondary columns below <c>sm</c>
/// (<c>hidden sm:table-cell</c>) and let the first cell carry the stacked detail instead. One markup, two
/// shapes — rather than a table and a card list that have to be kept saying the same thing.
/// </para>
/// </remarks>
public sealed partial class UiTable : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("overflow-x-auto rounded-xl border border-base-300 bg-base-100")[
            Table.Class("w-full border-collapse text-left text-sm")[Children ?? []]
        ];
}

/// <summary>The grid the overview lays its tiles on.</summary>
public sealed partial class UiGrid : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-3 sm:grid-cols-2 sm:gap-4 lg:grid-cols-3")[Children ?? []];
}
