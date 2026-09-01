using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The two formatters every panel repeats. Purely string-producing — it builds no markup at all, so it
/// needs nothing from the component surface; the panel states it used to carry are components below.
/// </summary>
internal static class DashboardParts
{
    /// <summary>
    /// A UTC instant as "how long ago", which is what an operator actually reads a queue timestamp for.
    /// Callers put the exact instant in the cell's Title, so hovering gives the precise value.
    /// </summary>
    public static string Ago(DateTime utc, DateTime now)
    {
        var delta = now - utc;
        if (delta < TimeSpan.Zero)
        {
            return "in " + Humanize(delta.Negate());
        }

        return delta < TimeSpan.FromSeconds(5) ? "just now" : Humanize(delta) + " ago";
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    public static string Duration(TimeSpan span) => Humanize(span);

    private static string Humanize(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)}m",
        { TotalHours: < 24 } => $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)}h",
        _ => $"{span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture)}d",
    };
}

/// <summary>
/// The console's shared class strings, named once so a panel does not spell a card out again.
/// </summary>
/// <remarks>
/// Strings rather than components where the shape varies: a card is a <c>div</c> with a border, and
/// wrapping every one of those in a type would buy indirection rather than meaning. What DOES get a
/// component is anything with behaviour or a fixed internal structure — see <c>Ui/</c>.
/// </remarks>
internal static class Ops
{
    /// <summary>A panel: hairline border, no shadow, and padding that tightens on a phone.</summary>
    public const string Card = "rounded-xl border border-ops-line bg-ops-panel p-4 sm:p-5";

    /// <summary>The small muted label above a value.</summary>
    public const string Label = "text-xs font-medium tracking-wide text-ops-muted";

    /// <summary>A headline number. Tabular figures so a polling value does not jitter as digits change.</summary>
    public const string Value = "mt-2 text-2xl font-semibold tabular-nums tracking-tight text-ops-ink sm:text-3xl";

    /// <summary>The quiet line under a value.</summary>
    public const string Caption = "mt-1 text-xs text-ops-muted";

    /// <summary>A page's own heading.</summary>
    public const string Heading = "text-base font-semibold tracking-tight text-ops-ink sm:text-lg";

    // min-h-11 below sm on every control: 44px is the smallest reliable touch target, and these are all
    // text-xs. The height relaxes from sm up, where there is a pointer.

    /// <summary>A secondary action.</summary>
    public const string Button =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-ops-line "
        + "bg-ops-bg px-3 text-xs font-medium text-ops-ink transition-colors hover:bg-ops-well "
        + "disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-ops-brand sm:min-h-0 sm:py-1.5";

    /// <summary>An action that destroys or re-runs work — the only colour on the console.</summary>
    public const string Danger =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-ops-line "
        + "bg-ops-bg px-3 text-xs font-medium text-ops-danger transition-colors hover:border-ops-danger/40 "
        + "hover:bg-ops-danger/5 disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-ops-brand sm:min-h-0 sm:py-1.5";

    /// <summary>A borderless control: a dismiss, a close.</summary>
    public const string Quiet =
        "inline-flex min-h-11 items-center rounded-lg px-2 text-xs text-ops-muted hover:bg-ops-well "
        + "hover:text-ops-ink sm:min-h-0 sm:py-1";

    /// <summary>Monospace, for ids and payloads.</summary>
    public const string Mono = "font-mono text-xs";
}

/// <summary>A bordered panel with an optional heading and an optional action in its corner.</summary>
internal sealed partial class OpsCard : Component
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
                Heading is null ? null : H2.Class(Ops.Heading)[Heading],
                Action
            ];

        return Div.Class($"{Ops.Card} {Class}")[header, Children ?? []];
    }
}

/// <summary>One number, with what it counts and where to go for the detail behind it.</summary>
internal sealed partial class OpsStat : Component
{
    public required string Value { get; set; }

    public required string Label { get; set; }

    // NULLABLE, not defaulted. A property with an initialiser is excluded from the chain altogether, and
    // one that is non-nullable without an initialiser becomes a REQUIRED step (RASK001) — so an optional
    // step is spelled by making the property nullable, and only that.
    public OpsIconName? Icon { get; set; }

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
            "danger" => "text-ops-danger",
            "warn" => "text-ops-warn-ink",
            _ => null,
        };

        Component body = Div.Class("flex items-start justify-between gap-3")[
            Div.Class("min-w-0")[
                Div.Class($"truncate {Ops.Label}")[Label],
                Div.Class(tone is null ? Ops.Value : $"{Ops.Value} {tone}")[Value],
                Caption is null ? null : Div.Class(Ops.Caption)[Caption]
            ],
            OpsIcon.Name(Icon ?? OpsIconName.Overview)
                .Class($"size-5 shrink-0 {tone ?? "text-ops-muted"}")
        ];

        // A tile that leads somewhere is a link, so it is reachable by keyboard and says where it goes —
        // rather than a div with a click handler, which is neither.
        return Href is { } href
            ? NavLink.Href(href).Class($"{Ops.Card} block no-underline transition-colors hover:bg-ops-well")[body]
            : Div.Class(Ops.Card)[body];
    }
}

/// <summary>A page heading with its caption and an optional row of controls.</summary>
internal sealed partial class OpsHeader : Component
{
    public required string Heading { get; set; }

    public string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <summary>Shown before the heading, so a queue page is recognisable at a glance.</summary>
    public OpsIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 sm:mb-5")[
            Icon is { } icon ? OpsIcon.Name(icon).Class("size-5 shrink-0 text-ops-muted") : null,
            H1.Class(Ops.Heading)[Heading],
            Caption is null ? null : Span.Class("text-xs text-ops-muted")[Caption],
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
internal sealed partial class OpsTabs : Component
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
internal sealed partial class OpsTab : Component
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
                ? "flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-lg border border-ops-line "
                  + "bg-ops-well px-3 text-sm font-medium text-ops-ink no-underline sm:min-h-0 sm:py-1.5"
                : "flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-lg border border-transparent "
                  + "px-3 text-sm text-ops-muted no-underline hover:bg-ops-well hover:text-ops-ink sm:min-h-0 "
                  + "sm:py-1.5")[
            Span[Label],
            Count is null
                ? null
                : Span.Class(Alarm == true
                    ? "rounded bg-ops-danger/10 px-1.5 py-0.5 text-xs tabular-nums text-ops-danger"
                    : "rounded bg-ops-well px-1.5 py-0.5 text-xs tabular-nums text-ops-muted")[Count]
        ];
}

/// <summary>An inline notice: a confirmation to answer, or the result of an action just taken.</summary>
internal sealed partial class OpsNotice : Component
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
        "danger" => "border-ops-danger/30 bg-ops-danger/5 text-ops-danger",
        "warn" => "border-ops-warn/40 bg-ops-warn/10 text-ops-ink",
        "info" => "border-ops-brand/30 bg-ops-brand/5 text-ops-ink",
        _ => "border-ops-line bg-ops-well text-ops-muted",
    };
}

/// <summary>A small status pill.</summary>
internal sealed partial class OpsBadge : Component
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
        "danger" => "bg-ops-danger/10 text-ops-danger",
        "warn" => "bg-ops-warn/15 text-ops-warn-ink",
        "info" => "bg-ops-brand/10 text-ops-brand",
        "ok" => "bg-ops-ok/10 text-ops-ok-ink",
        _ => "bg-ops-well text-ops-muted",
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
internal sealed partial class OpsTable : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("overflow-x-auto rounded-xl border border-ops-line bg-ops-panel")[
            Table.Class("w-full border-collapse text-left text-sm")[Children ?? []]
        ];
}

/// <summary>The grid the overview lays its tiles on.</summary>
internal sealed partial class OpsGrid : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-3 sm:grid-cols-2 sm:gap-4 lg:grid-cols-3")[Children ?? []];
}

/// <summary>The placeholder a panel shows while its first read is in flight.</summary>
internal sealed partial class DashboardLoading : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex items-center gap-2 py-8 text-sm text-ops-muted")[
            // A pure-CSS spinner: one more reason this package ships no assets.
            Span.Class("size-4 animate-spin rounded-full border-2 border-ops-line border-t-ops-muted")
                .Attributes(("aria-hidden", "true")),
            Span["Reading…"]
        ];
}

/// <summary>The empty state a panel shows when a read came back with nothing to display.</summary>
internal sealed partial class DashboardEmpty : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public required string Heading { get; set; }

    public required string Detail { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class($"{Ops.Card} text-center")[
            Div.Class("text-base font-medium text-ops-ink")[Heading],
            Div.Class("mt-1 text-sm text-ops-muted")[Detail]
        ];
}

/// <summary>
/// Shown when a read threw. A dashboard that silently stops updating is worse than one that says it
/// couldn't read, so the panel keeps its last values and puts the reason on top. Renders nothing when
/// there is no message, so a panel can hand it the read error unconditionally.
/// </summary>
internal sealed partial class DashboardError : Component
{
    public string? Message { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Message is null
            ? null
            : Div.Role("alert")
                .Class(
                    "mb-4 flex items-start gap-3 rounded-xl border border-ops-danger/30 bg-ops-danger/5 px-4 py-3 "
                    + "text-sm text-ops-danger")[
                OpsIcon.Name(OpsIconName.Warning).Class("mt-0.5 size-5 shrink-0"),
                Span.Class("min-w-0 break-words")["Couldn't read: ", Message]
            ];
}

/// <summary>
/// The notice a panel shows while its poll loop is parked, with the button that resumes it. Renders
/// nothing when the loop is running.
/// </summary>
internal sealed partial class DashboardParked : Component
{
    public bool Parked { get; set; }

    public Func<Task>? Resume { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Parked
            ? Div.Class("mt-4 flex flex-wrap items-center gap-3 text-xs text-ops-muted")[
                Span["Live updates paused to keep the database free."],
                Button.Type("button").Class(Ops.Button).OnClickAsync(ResumeAsync)["Resume"]
            ]
            : null;

    private Task ResumeAsync() => Resume?.Invoke() ?? Task.CompletedTask;
}
