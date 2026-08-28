using Rask.Html.Components;

namespace Rask.Dashboard;

/// <summary>The icons the operator dashboard draws. A closed set, because the dashboard owns its own chrome.</summary>
/// <remarks>
/// Named rather than free-form so a panel declares an icon that is certain to exist — a panel supplying a
/// string could name one that does not, and the dashboard would render a blank space with nothing
/// reporting it.
/// </remarks>
public enum OpsIconName
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
}

/// <summary>
/// One of <see cref="OpsIconName" />, drawn as inline SVG.
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
// In the root namespace beside the enum: OpsIconName is public panel API, and Rask.Dashboard.Pages and
// .Panels both see their parent namespace without a using of their own.
internal sealed partial class OpsIcon : Component
{
    /// <summary>Which icon to draw.</summary>
    public required OpsIconName Name { get; set; }

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
        OpsIconName.Overview => SvgPath.D(
            "M3.75 6A2.25 2.25 0 0 1 6 3.75h2.25A2.25 2.25 0 0 1 10.5 6v2.25a2.25 2.25 0 0 1-2.25 2.25H6a2.25 "
            + "2.25 0 0 1-2.25-2.25V6ZM3.75 15.75A2.25 2.25 0 0 1 6 13.5h2.25a2.25 2.25 0 0 1 2.25 2.25V18a2.25 "
            + "2.25 0 0 1-2.25 2.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H18A2.25 "
            + "2.25 0 0 1 20.25 6v2.25A2.25 2.25 0 0 1 18 10.5h-2.25a2.25 2.25 0 0 1-2.25-2.25V6ZM13.5 15.75a2.25 "
            + "2.25 0 0 1 2.25-2.25H18a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 18 20.25h-2.25A2.25 2.25 0 0 "
            + "1 13.5 18v-2.25Z"),

        OpsIconName.Queue => SvgPath.D(
            "M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 0 1 0 3.75H5.625a1.875 "
            + "1.875 0 0 1 0-3.75Z"),

        OpsIconName.Database => SvgPath.D(
            "M20.25 6.375c0 2.278-3.694 4.125-8.25 4.125S3.75 8.653 3.75 6.375m16.5 0c0-2.278-3.694-4.125-8.25-"
            + "4.125S3.75 4.097 3.75 6.375m16.5 0v11.25c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125V6.375"
            + "m16.5 0v3.75m-16.5-3.75v3.75m16.5 0v3.75C20.25 16.153 16.556 18 12 18s-8.25-1.847-8.25-4.125v-3.75"
            + "m16.5 0c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125"),

        OpsIconName.Envelope => SvgPath.D(
            "M21.75 6.75v10.5a2.25 2.25 0 0 1-2.25 2.25h-15a2.25 2.25 0 0 1-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0 0 "
            + "19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25m19.5 0v.243a2.25 2.25 0 0 1-1.07 1.916l-7.5 4.615a2.25 2.25 "
            + "0 0 1-2.36 0L3.32 8.91a2.25 2.25 0 0 1-1.07-1.916V6.75"),

        OpsIconName.Clock => SvgPath.D("M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"),

        OpsIconName.Retry => SvgPath.D(
            "M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-"
            + "3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182m0-4.991v4.99"),

        OpsIconName.Archive => SvgPath.D(
            "m20.25 7.5-.625 10.632a2.25 2.25 0 0 1-2.247 2.118H6.622a2.25 2.25 0 0 1-2.247-2.118L3.75 7.5M10 "
            + "11.25h4M3.375 7.5h17.25c.621 0 1.125-.504 1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125H3.375c-.621 "
            + "0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125Z"),

        OpsIconName.Outbox => SvgPath.D(
            "M6 12 3.269 3.125A59.769 59.769 0 0 1 21.485 12 59.768 59.768 0 0 1 3.27 20.875L5.999 12Zm0 0h7.5"),

        OpsIconName.Gear =>
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

        OpsIconName.Storage => SvgPath.D(
            "M5.25 14.25h13.5m-13.5 0a3 3 0 0 1-3-3m3 3a3 3 0 1 0 0 6h13.5a3 3 0 1 0 0-6m-16.5-3a3 3 0 0 1 3-3h13.5"
            + "a3 3 0 0 1 3 3m-19.5 0a4.5 4.5 0 0 1 .9-2.7L5.737 5.1a3.375 3.375 0 0 1 2.7-1.35h7.126c1.062 0 2.062."
            + "5 2.7 1.35l2.587 3.45a4.5 4.5 0 0 1 .9 2.7m0 0a3 3 0 0 1-3 3m0 3h.008v.008h-.008v-.008Zm0-6h.008v."
            + "008h-.008v-.008Zm-3 6h.008v.008h-.008v-.008Zm0-6h.008v.008h-.008v-.008Z"),

        OpsIconName.Warning => SvgPath.D(
            "M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 "
            + "3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"),

        OpsIconName.ShieldOk => SvgPath.D(
            "M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 "
            + "10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-"
            + "3.285Z"),

        OpsIconName.ShieldWarning => SvgPath.D(
            "M12 9v3.75m0-10.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.75c0 5.592 3.824 10.29 9 11.622 "
            + "5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.57-.598-3.75h-.152c-3.196 0-6.1-1.25-8.25-3.286Zm0 13.036h."
            + "008v.008H12v-.008Z"),

        // Unreachable for a declared name. A new enum member without a shape would otherwise draw an empty
        // box and read as a styling fault rather than a missing case.
        _ => throw new ArgumentOutOfRangeException(nameof(Name), Name, "No shape is defined for this icon."),
    };
}
