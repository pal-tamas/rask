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
/// component below is anything with behaviour or a fixed internal structure.
/// </remarks>
internal static class Ops
{
    /// <summary>A panel: hairline border, no shadow, generous padding.</summary>
    public const string Card = "rounded-xl border border-ops-line bg-ops-panel p-5";

    /// <summary>The small muted label above a value.</summary>
    public const string Label = "text-xs font-medium tracking-wide text-ops-muted";

    /// <summary>A headline number. Tabular figures so a polling value does not jitter as digits change.</summary>
    public const string Value = "mt-2 text-3xl font-semibold tabular-nums tracking-tight text-ops-ink";

    /// <summary>The quiet line under a value.</summary>
    public const string Caption = "mt-1 text-xs text-ops-muted";

    /// <summary>A page's own heading.</summary>
    public const string Heading = "text-lg font-semibold tracking-tight text-ops-ink";

    /// <summary>A secondary action.</summary>
    public const string Button =
        "inline-flex items-center gap-1.5 rounded-md border border-ops-line px-2.5 py-1.5 text-xs "
        + "font-medium text-ops-muted hover:border-ops-muted hover:text-ops-ink disabled:opacity-40";

    /// <summary>An action that destroys or re-runs work — the only colour on the console.</summary>
    public const string Danger =
        "inline-flex items-center gap-1.5 rounded-md bg-red-500/15 px-2.5 py-1.5 text-xs font-medium "
        + "text-red-300 hover:bg-red-500/25 disabled:opacity-40";

    /// <summary>A borderless control: a dismiss, a close.</summary>
    public const string Quiet = "rounded-md px-2 py-1 text-xs text-ops-muted hover:text-ops-ink";

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
            : Div.Class("mb-4 flex items-center justify-between gap-4")[
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
            "danger" => "text-red-400",
            "warn" => "text-amber-400",
            _ => null,
        };

        Component body = Div.Class("flex items-start justify-between gap-3")[
            Div[
                Div.Class(Ops.Label)[Label],
                Div.Class(tone is null ? Ops.Value : $"{Ops.Value} {tone}")[Value],
                Caption is null ? null : Div.Class(Ops.Caption)[Caption]
            ],
            OpsIcon.Name(Icon ?? OpsIconName.Overview)
                .Class($"size-5 shrink-0 {tone ?? "text-ops-muted"}")
        ];

        // A tile that leads somewhere is a link, so it is reachable by keyboard and says where it goes —
        // rather than a div with a click handler, which is neither.
        return Href is { } href
            ? NavLink.Href(href).Class($"{Ops.Card} block no-underline hover:border-ops-muted")[body]
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
        Div.Class("mb-5 flex flex-wrap items-center gap-x-3 gap-y-2")[
            Icon is { } icon ? OpsIcon.Name(icon).Class("size-5 shrink-0 text-ops-muted") : null,
            H1.Class(Ops.Heading)[Heading],
            Caption is null ? null : Span.Class("text-xs text-ops-muted")[Caption],
            Actions is null ? null : Div.Class("ml-auto")[Actions]
        ];
}

/// <summary>A row of link-shaped tabs. Navigation, so each one is a real link with a real URL.</summary>
internal sealed partial class OpsTabs : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class("flex flex-wrap items-center gap-1")[Children ?? []];
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
            // Same ops-nav-link contract the header nav carries: a tab IS a nav link here, and the
            // journeys select Live/History through it.
            .Class(Active == true
                ? "ops-nav-link flex items-center gap-2 rounded-md bg-ops-panel px-3 py-1.5 text-sm font-medium text-ops-ink no-underline"
                : "ops-nav-link flex items-center gap-2 rounded-md px-3 py-1.5 text-sm text-ops-muted no-underline hover:bg-ops-panel hover:text-ops-ink")[
            Span[Label],
            Count is null
                ? null
                : Span.Class(Alarm == true
                    ? "rounded bg-red-500/15 px-1.5 py-0.5 text-xs tabular-nums text-red-300"
                    : "rounded bg-white/5 px-1.5 py-0.5 text-xs tabular-nums text-ops-muted")[Count]
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
            .Class($"mb-4 flex flex-wrap items-center gap-3 rounded-lg border px-4 py-3 text-sm {Palette()}")[
            Children ?? []
        ];

    private string Palette() => Tone switch
    {
        "danger" => "border-red-500/40 bg-red-500/10 text-red-200",
        "warn" => "border-amber-500/40 bg-amber-500/10 text-amber-200",
        "info" => "border-sky-500/40 bg-sky-500/10 text-sky-200",
        _ => "border-ops-line bg-ops-panel text-ops-muted",
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
        Span.Class($"inline-flex items-center rounded px-1.5 py-0.5 text-xs font-medium {Palette()} {Class}")[Label];

    private string Palette() => Tone switch
    {
        "danger" => "bg-red-500/15 text-red-300",
        "warn" => "bg-amber-500/15 text-amber-300",
        "info" => "bg-sky-500/15 text-sky-300",
        "ok" => "bg-emerald-500/15 text-emerald-300",
        _ => "bg-white/5 text-ops-muted",
    };
}

/// <summary>
/// A table that scrolls on its own rather than making the page scroll sideways.
/// </summary>
/// <remarks>
/// The header row and the cell padding live here so every table on the console matches; a page supplies
/// only its <c>thead</c> and <c>tbody</c>.
/// </remarks>
internal sealed partial class OpsTable : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("overflow-x-auto rounded-xl border border-ops-line")[
            Table.Class("w-full border-collapse text-left text-sm")[Children ?? []]
        ];
}

/// <summary>The grid the overview lays its tiles on.</summary>
internal sealed partial class OpsGrid : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-4 sm:grid-cols-2 lg:grid-cols-4")[Children ?? []];
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
                .Class("mb-4 flex items-start gap-3 rounded-lg border border-red-500/40 bg-red-500/10 px-4 py-3 text-sm text-red-200")[
                OpsIcon.Name(OpsIconName.Warning).Class("mt-0.5 size-5 shrink-0"),
                Span["Couldn't read: ", Message]
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
            ? Div.Class("mt-4 flex items-center gap-3 text-xs text-ops-muted")[
                Span["Live updates paused to keep the database free."],
                Button.Type("button").Class(Ops.Button).OnClickAsync(ResumeAsync)["Resume"]
            ]
            : null;

    private Task ResumeAsync() => Resume?.Invoke() ?? Task.CompletedTask;
}
