using Rask.Html.Components;

namespace Rask.Ui;

/// <summary>The icons the operator dashboard draws. A closed set, because the dashboard owns its own chrome.</summary>
/// <remarks>
/// Named rather than free-form so a panel declares an icon that is certain to exist — a panel supplying a
/// string could name one that does not, and the dashboard would render a blank space with nothing
/// reporting it.
/// </remarks>
public enum UiIconName
{
    /// <summary>The overview page.</summary>
    Overview,

    /// <summary>A queue of work.</summary>
    Queue,

    /// <summary>The database.</summary>
    Database,

    /// <summary>Mail.</summary>
    Envelope,

    /// <summary>History, or an age.</summary>
    Clock,

    /// <summary>Retry.</summary>
    Retry,

    /// <summary>Retention, or something put away.</summary>
    Archive,

    /// <summary>Delivery out of the app.</summary>
    Outbox,

    /// <summary>Jobs.</summary>
    Gear,

    /// <summary>Storage.</summary>
    Storage,

    /// <summary>A warning.</summary>
    Warning,

    /// <summary>Secured.</summary>
    ShieldOk,

    /// <summary>Not secured.</summary>
    ShieldWarning,

    // Appended, never inserted: PublicAPI records each member by its ordinal, so putting a new name in
    // the middle silently renumbers every one below it and reads as a diff of unrelated members.

    /// <summary>Search.</summary>
    Search,

    /// <summary>Dismiss, or close an overlay.</summary>
    Close,

    /// <summary>A switcher: this value can be stepped to another.</summary>
    ChevronUpDown,

    /// <summary>Onwards — a breadcrumb separator, or a row that opens.</summary>
    ChevronRight,

    /// <summary>Delete.</summary>
    Trash,

    /// <summary>Done.</summary>
    Check,

    // The set below arrived with the landing site. The thirteen above were an operations vocabulary —
    // queues, retries, dead letters — which is the right set for a console and covers almost nothing a
    // page describing the framework needs to say. Same source and same style as the rest.

    /// <summary>Speed, or something generated.</summary>
    Bolt,

    /// <summary>Styling.</summary>
    PaintBrush,

    /// <summary>A form, checked.</summary>
    Clipboard,

    /// <summary>Authentication.</summary>
    Lock,

    /// <summary>Two directions — a request and its answer.</summary>
    ArrowsRightLeft,

    /// <summary>A phone, or anything installable onto one.</summary>
    Phone,

    /// <summary>A platform capability.</summary>
    Cube,

    /// <summary>A slice of an application, or a set of layers.</summary>
    Stack,

    /// <summary>Shipping.</summary>
    Rocket,

    /// <summary>A notification.</summary>
    Bell,

    /// <summary>A server.</summary>
    Server,

    /// <summary>The browser, or the web at large.</summary>
    Globe,

    /// <summary>A command line.</summary>
    Terminal,

    /// <summary>A link that leaves this site.</summary>
    ExternalLink,

    /// <summary>A favourite.</summary>
    Star,

    /// <summary>Something that drops into a page that was not built for it.</summary>
    Puzzle,

    /// <summary>Something produced ahead of time.</summary>
    Sparkles,
}

/// <summary>
/// One of <see cref="UiIconName" />, drawn as inline SVG.
/// </summary>
/// <remarks>
/// <para>
/// The geometry is <see href="https://heroicons.com">Heroicons</see> v2 (outline), MIT-licensed, by the
/// makers of Tailwind — which is what the rest of this dashboard is styled with. Vendored as path data
/// rather than taken as a dependency: thirteen icons is not worth a package, and inlining them means the
/// dashboard carries no icon font, no stylesheet and no static assets at all. That is what lets it ship as
/// a plain assembly and is why <c>Rask.Bootstrap</c> is no longer in its graph.
/// </para>
/// <para>
/// Stroked in <c>currentColor</c>, so an icon takes the colour of whatever it sits in.
/// <c>aria-hidden</c> throughout: every one of these sits beside a text label, and a screen reader that
/// announced it would only repeat the label.
/// </para>
/// </remarks>
// In the root namespace beside the enum: UiIconName is public panel API, and Rask.Dashboard.Pages and
// .Panels both see their parent namespace without a using of their own.
public sealed partial class UiIcon : Component
{
    /// <summary>Which icon to draw.</summary>
    public required UiIconName Name { get; set; }

    /// <summary>Extra classes, for sizing at the call site.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Svg.ViewBox("0 0 24 24")
            .Fill("none")
            .Stroke("currentColor")
            .StrokeWidth("1.5")
            .StrokeLinecap("round")
            .Attributes(("stroke-linejoin", "round"), ("aria-hidden", "true"), ("focusable", "false"))
            .Class(Class ?? "size-5 shrink-0")[
            Shapes()
        ];

    private Component Shapes() => Name switch
    {
        UiIconName.Overview => SvgPath.D(
            "M3.75 6A2.25 2.25 0 0 1 6 3.75h2.25A2.25 2.25 0 0 1 10.5 6v2.25a2.25 2.25 0 0 1-2.25 2.25H6a2.25 "
            + "2.25 0 0 1-2.25-2.25V6ZM3.75 15.75A2.25 2.25 0 0 1 6 13.5h2.25a2.25 2.25 0 0 1 2.25 2.25V18a2.25 "
            + "2.25 0 0 1-2.25 2.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H18A2.25 "
            + "2.25 0 0 1 20.25 6v2.25A2.25 2.25 0 0 1 18 10.5h-2.25a2.25 2.25 0 0 1-2.25-2.25V6ZM13.5 15.75a2.25 "
            + "2.25 0 0 1 2.25-2.25H18a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 18 20.25h-2.25A2.25 2.25 0 0 "
            + "1 13.5 18v-2.25Z"),

        UiIconName.Queue => SvgPath.D(
            "M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 0 1 0 3.75H5.625a1.875 "
            + "1.875 0 0 1 0-3.75Z"),

        UiIconName.Database => SvgPath.D(
            "M20.25 6.375c0 2.278-3.694 4.125-8.25 4.125S3.75 8.653 3.75 6.375m16.5 0c0-2.278-3.694-4.125-8.25-"
            + "4.125S3.75 4.097 3.75 6.375m16.5 0v11.25c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125V6.375"
            + "m16.5 0v3.75m-16.5-3.75v3.75m16.5 0v3.75C20.25 16.153 16.556 18 12 18s-8.25-1.847-8.25-4.125v-3.75"
            + "m16.5 0c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125"),

        UiIconName.Envelope => SvgPath.D(
            "M21.75 6.75v10.5a2.25 2.25 0 0 1-2.25 2.25h-15a2.25 2.25 0 0 1-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0 0 "
            + "19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25m19.5 0v.243a2.25 2.25 0 0 1-1.07 1.916l-7.5 4.615a2.25 2.25 "
            + "0 0 1-2.36 0L3.32 8.91a2.25 2.25 0 0 1-1.07-1.916V6.75"),

        UiIconName.Clock => SvgPath.D("M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"),

        UiIconName.Retry => SvgPath.D(
            "M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-"
            + "3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182m0-4.991v4.99"),

        UiIconName.Archive => SvgPath.D(
            "m20.25 7.5-.625 10.632a2.25 2.25 0 0 1-2.247 2.118H6.622a2.25 2.25 0 0 1-2.247-2.118L3.75 7.5M10 "
            + "11.25h4M3.375 7.5h17.25c.621 0 1.125-.504 1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125H3.375c-.621 "
            + "0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125Z"),

        UiIconName.Outbox => SvgPath.D(
            "M6 12 3.269 3.125A59.769 59.769 0 0 1 21.485 12 59.768 59.768 0 0 1 3.27 20.875L5.999 12Zm0 0h7.5"),

        UiIconName.Gear =>
        [
            SvgPath.D(
                "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645."
                + "87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 "
                + "2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255"
                + "c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369."
                + "491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644."
                + "869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-"
                + ".312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 "
                + "0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6."
                + "932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-"
                + "2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-."
                + "128.332-.183.582-.495.644-.869l.214-1.28Z"),
            SvgPath.D("M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z"),
        ],

        UiIconName.Storage => SvgPath.D(
            "M5.25 14.25h13.5m-13.5 0a3 3 0 0 1-3-3m3 3a3 3 0 1 0 0 6h13.5a3 3 0 1 0 0-6m-16.5-3a3 3 0 0 1 3-3h13.5"
            + "a3 3 0 0 1 3 3m-19.5 0a4.5 4.5 0 0 1 .9-2.7L5.737 5.1a3.375 3.375 0 0 1 2.7-1.35h7.126c1.062 0 2.062."
            + "5 2.7 1.35l2.587 3.45a4.5 4.5 0 0 1 .9 2.7m0 0a3 3 0 0 1-3 3m0 3h.008v.008h-.008v-.008Zm0-6h.008v."
            + "008h-.008v-.008Zm-3 6h.008v.008h-.008v-.008Zm0-6h.008v.008h-.008v-.008Z"),

        UiIconName.Warning => SvgPath.D(
            "M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 "
            + "3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"),

        UiIconName.ShieldOk => SvgPath.D(
            "M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 "
            + "10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-"
            + "3.285Z"),

        UiIconName.ShieldWarning => SvgPath.D(
            "M12 9v3.75m0-10.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.75c0 5.592 3.824 10.29 9 11.622 "
            + "5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.57-.598-3.75h-.152c-3.196 0-6.1-1.25-8.25-3.286Zm0 13.036h."
            + "008v.008H12v-.008Z"),

        UiIconName.Search => SvgPath.D(
            "m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z"),

        UiIconName.Close => SvgPath.D("M6 18 18 6M6 6l12 12"),

        UiIconName.ChevronUpDown => SvgPath.D("M8.25 15 12 18.75 15.75 15m-7.5-6L12 5.25 15.75 9"),

        UiIconName.ChevronRight => SvgPath.D("m8.25 4.5 7.5 7.5-7.5 7.5"),

        UiIconName.Trash => SvgPath.D(
            "m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 "
            + "2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3."
            + "478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-."
            + "91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48."
            + "667 0 0 0-7.5 0"),

        UiIconName.Check => SvgPath.D("m4.5 12.75 6 6 9-13.5"),

        UiIconName.Bolt => SvgPath.D("m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z"),

        UiIconName.PaintBrush => SvgPath.D(
            "M9.53 16.122a3 3 0 0 0-5.78 1.128 2.25 2.25 0 0 1-2.4 2.245 4.5 4.5 0 0 0 8.4-2.245c0-.399-.078-"
            + ".78-.22-1.128Zm0 0a15.998 15.998 0 0 0 3.388-1.62m-5.043-.025a15.994 15.994 0 0 1 1.622-3.395m3.42 "
            + "3.42a15.995 15.995 0 0 0 4.764-4.648l3.876-5.814a1.151 1.151 0 0 0-1.597-1.597L14.146 6.32a15.996 "
            + "15.996 0 0 0-4.649 4.763m3.42 3.42a6.776 6.776 0 0 0-3.42-3.42"),

        UiIconName.Clipboard => SvgPath.D(
            "M11.35 3.836c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 0 0 .75-.75 2.25 2.25 0 0 0-.1-"
            + ".664m-5.8 0A2.251 2.251 0 0 1 13.5 2.25H15c1.012 0 1.867.668 2.15 1.586m-5.8 0c-.376.023-.75.05-"
            + "1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m8.9-4.414c.376.023.75.05 1.124.08 1.131.094 1.976 "
            + "1.057 1.976 2.192V16.5A2.25 2.25 0 0 1 18 18.75h-2.25m-7.5-10.5H4.875c-.621 0-1.125.504-1.125 "
            + "1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V18.75m-7.5-10.5h6.375c"
            + ".621 0 1.125.504 1.125 1.125v9.375m-8.25-3 1.5 1.5 3-3.75"),

        UiIconName.Lock => SvgPath.D(
            "M16.5 10.5V6.75a4.5 4.5 0 1 0-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25 0 0 "
            + "0-2.25-2.25H6.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z"),

        UiIconName.ArrowsRightLeft => SvgPath.D("M7.5 21 3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5"),

        UiIconName.Phone => SvgPath.D(
            "M10.5 1.5H8.25A2.25 2.25 0 0 0 6 3.75v16.5a2.25 2.25 0 0 0 2.25 2.25h7.5A2.25 2.25 0 0 0 18 20.25V3."
            + "75a2.25 2.25 0 0 0-2.25-2.25H13.5m-3 0V3h3V1.5m-3 0h3m-3 18.75h3"),

        UiIconName.Cube => SvgPath.D(
            "m21 7.5-2.25-1.313M21 7.5v2.25m0-2.25-2.25 1.313M3 7.5l2.25-1.313M3 7.5l2.25 1.313M3 7.5v2.25m9 3 "
            + "2.25-1.313M12 12.75l-2.25-1.313M12 12.75V15m0 6.75 2.25-1.313M12 21.75V19.5m0 2.25-2.25-1.313m0-"
            + "16.875L12 2.25l2.25 1.313M21 14.25v2.25l-2.25 1.313m-13.5 0L3 16.5v-2.25"),

        UiIconName.Stack => SvgPath.D(
            "M6 6.878V6a2.25 2.25 0 0 1 2.25-2.25h7.5A2.25 2.25 0 0 1 18 6v.878m-12 0c.235-.083.487-.128.75-.128h10"
            + ".5c.263 0 .515.045.75.128m-12 0A2.25 2.25 0 0 0 4.5 9v.878m13.5-3A2.25 2.25 0 0 1 19.5 9v.878m0 0a2"
            + ".246 2.246 0 0 0-.75-.128H5.25c-.263 0-.515.045-.75.128m15 0A2.25 2.25 0 0 1 21 12v6a2.25 2.25 0 0 "
            + "1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 18v-6c0-.98.626-1.813 1.5-2.122"),

        UiIconName.Rocket => SvgPath.D(
            "M15.59 14.37a6 6 0 0 1-5.84 7.38v-4.8m5.84-2.58a14.98 14.98 0 0 0 6.16-12.12A14.98 14.98 0 0 0 9.631 "
            + "8.41m5.96 5.96a14.926 14.926 0 0 1-5.841 2.58m-.119-8.54a6 6 0 0 0-7.381 5.84h4.8m2.581-5.84a14.927 "
            + "14.927 0 0 0-2.58 5.84m2.699 2.7c-.103.021-.207.041-.311.06a15.09 15.09 0 0 1-2.448-2.448 14.9 14.9 "
            + "0 0 1 .06-.312m-2.24 2.39a4.493 4.493 0 0 0-1.757 4.306 4.493 4.493 0 0 0 4.306-1.758M16.5 9a1.5 1.5 "
            + "0 1 1-3 0 1.5 1.5 0 0 1 3 0Z"),

        UiIconName.Bell => SvgPath.D(
            "M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 "
            + "8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 "
            + "0a3 3 0 1 1-5.714 0M3.124 7.5A8.969 8.969 0 0 1 5.292 3m13.416 0a8.969 8.969 0 0 1 2.168 4.5"),

        UiIconName.Server => SvgPath.D(
            "M5.25 14.25h13.5m-13.5 0a3 3 0 0 1-3-3m3 3a3 3 0 1 0 0 6h13.5a3 3 0 1 0 0-6m-16.5-3a3 3 0 0 1 3-3h13"
            + ".5a3 3 0 0 1 3 3m-19.5 0a4.5 4.5 0 0 1 .9-2.7L5.737 5.1a3.375 3.375 0 0 1 2.7-1.35h7.126c1.062 0 "
            + "2.062.5 2.7 1.35l2.587 3.45a4.5 4.5 0 0 1 .9 2.7m0 0a3 3 0 0 1-3 3m0 3h.008v.008h-.008v-.008Zm0-6h"
            + ".008v.008h-.008v-.008Zm-3 6h.008v.008h-.008v-.008Zm0-6h.008v.008h-.008v-.008Z"),

        UiIconName.Globe => SvgPath.D(
            "M12 21a9.004 9.004 0 0 0 8.716-6.747M12 21a9.004 9.004 0 0 1-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-"
            + "9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 0 1 7.843 4.582M12 3a8"
            + ".997 8.997 0 0 0-7.843 4.582m15.686 0A11.953 11.953 0 0 1 12 10.5c-2.998 0-5.74-1.1-7.843-2.918m15"
            + ".686 0A8.959 8.959 0 0 1 21 12c0 .778-.099 1.533-.284 2.253m0 0A17.919 17.919 0 0 1 12 16.5c-3.162 "
            + "0-6.133-.815-8.716-2.247m0 0A9.015 9.015 0 0 1 3 12c0-1.605.42-3.113 1.157-4.418"),

        UiIconName.Terminal => SvgPath.D(
            "m6.75 7.5 3 2.25-3 2.25m4.5 0h3m-9 8.25h13.5A2.25 2.25 0 0 0 21 18V6a2.25 2.25 0 0 0-2.25-2.25H5.25A2"
            + ".25 2.25 0 0 0 3 6v12a2.25 2.25 0 0 0 2.25 2.25Z"),

        UiIconName.ExternalLink => SvgPath.D(
            "M13.5 6H5.25A2.25 2.25 0 0 0 3 8.25v10.5A2.25 2.25 0 0 0 5.25 21h10.5A2.25 2.25 0 0 0 18 18.75V10.5m-"
            + "10.5 6L21 3m0 0h-5.25M21 3v5.25"),

        UiIconName.Star => SvgPath.D(
            "M11.48 3.499a.562.562 0 0 1 1.04 0l2.125 5.111a.563.563 0 0 0 .475.345l5.518.442c.499.04.701.663.321."
            + "988l-4.204 3.602a.563.563 0 0 0-.182.557l1.285 5.385a.562.562 0 0 1-.84.61l-4.725-2.885a.562.562 0 "
            + "0 0-.586 0L6.982 20.54a.562.562 0 0 1-.84-.61l1.285-5.386a.562.562 0 0 0-.182-.557l-4.204-3.602a."
            + "562.562 0 0 1 .321-.988l5.518-.442a.563.563 0 0 0 .475-.345L11.48 3.5Z"),

        UiIconName.Puzzle => SvgPath.D(
            "M14.25 6.087c0-.355.186-.676.401-.959.221-.29.349-.634.349-1.003 0-1.036-1.007-1.875-2.25-1.875s-2.25"
            + ".84-2.25 1.875c0 .369.128.713.349 1.003.215.283.401.604.401.959v0a.64.64 0 0 1-.657.643 48.39 48.39 "
            + "0 0 1-4.163-.3c.186 1.613.293 3.25.315 4.907a.656.656 0 0 1-.658.663v0c-.355 0-.676-.186-.959-.401a1"
            + ".647 1.647 0 0 0-1.003-.349c-1.036 0-1.875 1.007-1.875 2.25s.84 2.25 1.875 2.25c.369 0 .713-.128 1."
            + "003-.349.283-.215.604-.401.959-.401v0c.31 0 .555.26.532.57a48.039 48.039 0 0 1-.642 5.056c1.518.19 "
            + "3.058.309 4.616.354a.64.64 0 0 0 .657-.643v0c0-.355-.186-.676-.401-.959a1.647 1.647 0 0 1-.349-1.003"
            + "c0-1.035 1.008-1.875 2.25-1.875 1.243 0 2.25.84 2.25 1.875 0 .369-.128.713-.349 1.003-.215.283-.4."
            + "604-.4.959v0c0 .333.277.599.61.58a48.1 48.1 0 0 0 5.427-.63 48.05 48.05 0 0 0 .582-4.717.532.532 0 "
            + "0 0-.533-.57v0c-.355 0-.676.186-.959.401-.29.221-.634.349-1.003.349-1.035 0-1.875-1.007-1.875-2.25s"
            + ".84-2.25 1.875-2.25c.37 0 .713.128 1.003.349.283.215.604.401.96.401v0a.656.656 0 0 0 .658-.663 48."
            + "422 48.422 0 0 0-.37-5.36c-1.886.342-3.81.574-5.766.689a.578.578 0 0 1-.61-.58v0Z"),

        UiIconName.Sparkles => SvgPath.D(
            "M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 "
            + "5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 "
            + "9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2."
            + "25l.259 1.035a3.375 3.375 0 0 0 2.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 0 0-2.456 2.456ZM16.894 "
            + "20.567 16.5 21.75l-.394-1.183a2.25 2.25 0 0 0-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 0 0 1.423"
            + "-1.423l.394-1.183.394 1.183a2.25 2.25 0 0 0 1.423 1.423l1.183.394-1.183.394a2.25 2.25 0 0 0-1.423 1."
            + "423Z"),

        // Unreachable for a declared name. A new enum member without a shape would otherwise draw an empty
        // box and read as a styling fault rather than a missing case.
        _ => throw new ArgumentOutOfRangeException(nameof(Name), Name, "No shape is defined for this icon."),
    };
}
