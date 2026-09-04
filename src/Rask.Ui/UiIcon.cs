using Rask.Html.Components;

namespace Rask.Ui;

/// <summary>The icons the kit draws. A closed set.</summary>
/// <remarks>
/// <para>
/// Named rather than free-form so a caller declares an icon that is certain to exist — a caller supplying
/// a string could name one that does not, and the surface would render a blank space with nothing
/// reporting it.
/// </para>
/// <para>
/// Closed, and deliberately smaller than the set it replaced. The showcase used to carry 132
/// Bootstrap-shaped members, 67 of which existed only to give each guide its own glyph; those collapsed
/// onto one icon per guide GROUP. What is here is what a surface actually needs to SAY — actions,
/// states, controls — rather than a decorative spectrum. Adding a member is cheap; adding one that
/// duplicates the meaning of another is what makes an icon set stop being a vocabulary.
/// </para>
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

    /// <summary>Open the navigation on a small screen.</summary>
    Menu,

    /// <summary>Run it.</summary>
    Play,

    /// <summary>Documentation.</summary>
    Book,

    // Added when the showcase retired its own 132-member Bootstrap-shaped set. Same source and
    // style as the rest: Heroicons v2 outline, MIT, vendored as path data.

    /// <summary>Send it.</summary>
    PaperAirplane,

    /// <summary>Step back.</summary>
    Undo,

    /// <summary>Reorder.</summary>
    ArrowsUpDown,

    /// <summary>Back.</summary>
    ArrowLeft,

    /// <summary>Onward.</summary>
    ArrowRight,

    /// <summary>Upward.</summary>
    ArrowUp,

    /// <summary>A basket of goods.</summary>
    ShoppingBag,

    /// <summary>A radio link — Bluetooth, a broadcast.</summary>
    Signal,

    /// <summary>A defect.</summary>
    Bug,

    /// <summary>A date, or a schedule.</summary>
    Calendar,

    /// <summary>A camera.</summary>
    VideoCamera,

    /// <summary>Confirmed.</summary>
    CheckCircle,

    /// <summary>Payment.</summary>
    CreditCard,

    /// <summary>Remove one.</summary>
    Minus,

    /// <summary>A screen.</summary>
    Desktop,

    /// <summary>Save it locally.</summary>
    Download,

    /// <summary>Pick a colour.</summary>
    EyeDropper,

    /// <summary>A file.</summary>
    Document,

    /// <summary>A passkey, or biometrics.</summary>
    FingerPrint,

    /// <summary>A directory.</summary>
    Folder,

    /// <summary>Something given.</summary>
    Gift,

    /// <summary>Source.</summary>
    CodeBracket,

    /// <summary>A drag handle.</summary>
    Grip,

    /// <summary>A pointer, or a gesture.</summary>
    Cursor,

    /// <summary>The start.</summary>
    Home,

    /// <summary>An aside.</summary>
    Info,

    /// <summary>Night, or a dark theme.</summary>
    Moon,

    /// <summary>Edit.</summary>
    Pencil,

    /// <summary>Add somebody.</summary>
    UserPlus,

    /// <summary>Add one.</summary>
    Plus,

    /// <summary>Persist it.</summary>
    Save,

    /// <summary>Halt.</summary>
    Stop,

    /// <summary>A token, or an entry.</summary>
    Ticket,

    /// <summary>Unsecured.</summary>
    Unlock,

    /// <summary>A section that opens.</summary>
    ChevronDown,

    /// <summary>A section that closes.</summary>
    ChevronUp,

    /// <summary>Fill the screen.</summary>
    Fullscreen,

    /// <summary>A credential.</summary>
    Key,
}

/// <summary>
/// One of <see cref="UiIconName" />, drawn as inline SVG.
/// </summary>
/// <remarks>
/// <para>
/// The geometry is <see href="https://heroicons.com">Heroicons</see> v2 (outline), MIT-licensed, by the
/// makers of Tailwind — which is what the kit is styled with. Vendored as path data rather than taken as
/// a dependency: inlining means this package carries no icon font, no stylesheet and no static assets at
/// all, which is what lets it ship as a plain assembly.
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

    /// <summary>
    ///     Extra classes for the call site. ADDITIVE: these are appended to the icon's own sizing
    ///     (<c>size-5 shrink-0</c>) rather than replacing it, so a caller can add a margin or a colour
    ///     without having to restate a size. Naming a size — any <c>size-*</c>, <c>w-*</c> or <c>h-*</c>
    ///     utility — suppresses the default instead of competing with it.
    /// </summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Svg.ViewBox("0 0 24 24")
            .Fill("none")
            .Stroke("currentColor")
            .StrokeWidth("1.5")
            .StrokeLinecap("round")
            .Attributes(("stroke-linejoin", "round"), ("aria-hidden", "true"), ("focusable", "false"))
            .Class(ComposeClass())[
            Shapes()
        ];

    /// <summary>
    ///     The icon's own sizing plus whatever the call site asked for.
    /// </summary>
    /// <remarks>
    ///     The default is DROPPED rather than merged when the caller names a size, because two competing
    ///     Tailwind size utilities on one element are resolved by stylesheet order, not by the order they
    ///     appear in the attribute — so merging would make the rendered size depend on how the sheet was
    ///     generated.
    ///     <para>
    ///         Applying it at all is the load-bearing part. This property used to REPLACE the sizing, which
    ///         reads as harmless until you remember an inline SVG has no intrinsic size the way a text glyph
    ///         does: a caller adding <c>me-1</c> for a margin got an icon with no width or height, which does
    ///         not render small or unstyled, it renders as nothing. Nothing catches that downstream either —
    ///         markup assertions see the class list they expected, and a browser test reports only
    ///         "element is not visible", which points at the page rather than at here.
    ///     </para>
    /// </remarks>
    private string ComposeClass()
    {
        const string ownSizing = "size-5 shrink-0";
        if (string.IsNullOrWhiteSpace(Class))
        {
            return ownSizing;
        }

        return NamesASize(Class) ? Class : ownSizing + " " + Class;
    }

    private static bool NamesASize(string classes)
    {
        foreach (var token in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Tailwind sizing utilities, including their variant-prefixed (`md:size-4`) and negative
            // forms. Matching the prefix is enough: nothing else in the vocabulary starts this way.
            var bare = token[(token.LastIndexOf(':') + 1)..].TrimStart('-');
            if (bare.StartsWith("size-", StringComparison.Ordinal)
                || bare.StartsWith("w-", StringComparison.Ordinal)
                || bare.StartsWith("h-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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

        UiIconName.Menu => SvgPath.D("M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5"),

        UiIconName.Play => SvgPath.D(
            "M5.25 5.653c0-.856.917-1.398 1.667-.986l11.54 6.347a1.125 1.125 0 0 1 0 1.972l-11.54 6.347a1.125 "
            + "1.125 0 0 1-1.667-.986V5.653Z"),

        UiIconName.Book => SvgPath.D(
            "M12 6.042A8.967 8.967 0 0 0 6 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 0 1 6 18c2.305 0 "
            + "4.408.867 6 2.292m0-14.25a8.966 8.966 0 0 1 6-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0 "
            + "0 18 18a8.967 8.967 0 0 0-6 2.292m0-14.25v14.25"),

        UiIconName.PaperAirplane => SvgPath.D(
            "M6 12 3.269 3.125A59.769 59.769 0 0 1 21.485 12 59.768 59.768 0 0 1 3.27 20.875L5.999 12Zm0 0h7."
            + "5"),

        UiIconName.Undo => SvgPath.D("M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3"),

        UiIconName.ArrowsUpDown => SvgPath.D("M3 7.5 7.5 3m0 0L12 7.5M7.5 3v13.5m13.5 0L16.5 21m0 0L12 16.5m4.5 4.5V7.5"),

        UiIconName.ArrowLeft => SvgPath.D("M10.5 19.5 3 12m0 0 7.5-7.5M3 12h18"),

        UiIconName.ArrowRight => SvgPath.D("M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3"),

        UiIconName.ArrowUp => SvgPath.D("M4.5 10.5 12 3m0 0 7.5 7.5M12 3v18"),

        UiIconName.ShoppingBag => SvgPath.D(
            "M15.75 10.5V6a3.75 3.75 0 1 0-7.5 0v4.5m11.356-1.993 1.263 12c.07.665-.45 1.243-1.119 1.243H4.25"
            + "a1.125 1.125 0 0 1-1.12-1.243l1.264-12A1.125 1.125 0 0 1 5.513 7.5h12.974c.576 0 1.059.435 1.119"
            + " 1.007ZM8.625 10.5a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm7.5 0a.375.375 0 1 1-.75 0 .375.37"
            + "5 0 0 1 .75 0Z"),

        UiIconName.Signal => SvgPath.D(
            "M9.348 14.652a3.75 3.75 0 0 1 0-5.304m5.304 0a3.75 3.75 0 0 1 0 5.304m-7.425 2.121a6.75 6.75 0 0"
            + " 1 0-9.546m9.546 0a6.75 6.75 0 0 1 0 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 "
            + "0c3.808 3.807 3.808 9.98 0 13.788M12 12h.008v.008H12V12Zm.375 0a.375.375 0 1 1-.75 0 .375.375 0 "
            + "0 1 .75 0Z"),

        UiIconName.Bug => SvgPath.D(
            "M12 12.75c1.148 0 2.278.08 3.383.237 1.037.146 1.866.966 1.866 2.013 0 3.728-2.35 6.75-5.25 6.75"
            + "S6.75 18.728 6.75 15c0-1.046.83-1.867 1.866-2.013A24.204 24.204 0 0 1 12 12.75Zm0 0c2.883 0 5.64"
            + "7.508 8.207 1.44a23.91 23.91 0 0 1-1.152 6.06M12 12.75c-2.883 0-5.647.508-8.208 1.44.125 2.104.5"
            + "2 4.136 1.153 6.06M12 12.75a2.25 2.25 0 0 0 2.248-2.354M12 12.75a2.25 2.25 0 0 1-2.248-2.354M12 "
            + "8.25c.995 0 1.971-.08 2.922-.236.403-.066.74-.358.795-.762a3.778 3.778 0 0 0-.399-2.25M12 8.25c-"
            + ".995 0-1.97-.08-2.922-.236-.402-.066-.74-.358-.795-.762a3.734 3.734 0 0 1 .4-2.253M12 8.25a2.25 "
            + "2.25 0 0 0-2.248 2.146M12 8.25a2.25 2.25 0 0 1 2.248 2.146M8.683 5a6.032 6.032 0 0 1-1.155-1.002"
            + "c.07-.63.27-1.222.574-1.747m.581 2.749A3.75 3.75 0 0 1 15.318 5m0 0c.427-.283.815-.62 1.155-.999"
            + "a4.471 4.471 0 0 0-.575-1.752M4.921 6a24.048 24.048 0 0 0-.392 3.314c1.668.546 3.416.914 5.223 1"
            + ".082M19.08 6c.205 1.08.337 2.187.392 3.314a23.882 23.882 0 0 1-5.223 1.082"),

        UiIconName.Calendar => SvgPath.D(
            "M6.75 3v2.25M17.25 3v2.25M3 18.75V7.5a2.25 2.25 0 0 1 2.25-2.25h13.5A2.25 2.25 0 0 1 21 7.5v11.2"
            + "5m-18 0A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75m-18 0v-7.5A2.25 2.25 0 0 1 5.25 9h"
            + "13.5A2.25 2.25 0 0 1 21 11.25v7.5m-9-6h.008v.008H12v-.008ZM12 15h.008v.008H12V15Zm0 2.25h.008v.0"
            + "08H12v-.008ZM9.75 15h.008v.008H9.75V15Zm0 2.25h.008v.008H9.75v-.008ZM7.5 15h.008v.008H7.5V15Zm0 "
            + "2.25h.008v.008H7.5v-.008Zm6.75-4.5h.008v.008h-.008v-.008Zm0 2.25h.008v.008h-.008V15Zm0 2.25h.008"
            + "v.008h-.008v-.008Zm2.25-4.5h.008v.008H16.5v-.008Zm0 2.25h.008v.008H16.5V15Z"),

        UiIconName.VideoCamera => SvgPath.D(
            "m15.75 10.5 4.72-4.72a.75.75 0 0 1 1.28.53v11.38a.75.75 0 0 1-1.28.53l-4.72-4.72M4.5 18.75h9a2.2"
            + "5 2.25 0 0 0 2.25-2.25v-9a2.25 2.25 0 0 0-2.25-2.25h-9A2.25 2.25 0 0 0 2.25 7.5v9a2.25 2.25 0 0 "
            + "0 2.25 2.25Z"),

        UiIconName.CheckCircle => SvgPath.D("M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"),

        UiIconName.CreditCard => SvgPath.D(
            "M2.25 8.25h19.5M2.25 9h19.5m-16.5 5.25h6m-6 2.25h3m-3.75 3h15a2.25 2.25 0 0 0 2.25-2.25V6.75A2.2"
            + "5 2.25 0 0 0 19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25v10.5A2.25 2.25 0 0 0 4.5 19.5Z"),

        UiIconName.Minus => SvgPath.D("M5 12h14"),

        UiIconName.Desktop => SvgPath.D(
            "M9 17.25v1.007a3 3 0 0 1-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0 1 15 18.257V17.25m6-12V15a2.25 2"
            + ".25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 15V5.25m18 0A2.25 2.25 0 0 0 18.75 3H5.25A2.25 2.25 0"
            + " 0 0 3 5.25m18 0V12a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 12V5.25"),

        UiIconName.Download => SvgPath.D(
            "M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5M16.5 12 12 16.5m0 0L7.5 "
            + "12m4.5 4.5V3"),

        UiIconName.EyeDropper => SvgPath.D(
            "m15 11.25 1.5 1.5.75-.75V8.758l2.276-.61a3 3 0 1 0-3.675-3.675l-.61 2.277H12l-.75.75 1.5 1.5M15 "
            + "11.25l-8.47 8.47c-.34.34-.8.53-1.28.53s-.94.19-1.28.53l-.97.97-.75-.75.97-.97c.34-.34.53-.8.53-1"
            + ".28s.19-.94.53-1.28L12.75 9M15 11.25 12.75 9"),

        UiIconName.Document => SvgPath.D(
            "M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3."
            + "375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v1"
            + "7.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z"),

        UiIconName.FingerPrint => SvgPath.D(
            "M7.864 4.243A7.5 7.5 0 0 1 19.5 10.5c0 2.92-.556 5.709-1.568 8.268M5.742 6.364A7.465 7.465 0 0 0"
            + " 4.5 10.5a7.464 7.464 0 0 1-1.15 3.993m1.989 3.559A11.209 11.209 0 0 0 8.25 10.5a3.75 3.75 0 1 1"
            + " 7.5 0c0 .527-.021 1.049-.064 1.565M12 10.5a14.94 14.94 0 0 1-3.6 9.75m6.633-4.596a18.666 18.666"
            + " 0 0 1-2.485 5.33"),

        UiIconName.Folder => SvgPath.D(
            "M3.75 9.776c.112-.017.227-.026.344-.026h15.812c.117 0 .232.009.344.026m-16.5 0a2.25 2.25 0 0 0-1"
            + ".883 2.542l.857 6a2.25 2.25 0 0 0 2.227 1.932H19.05a2.25 2.25 0 0 0 2.227-1.932l.857-6a2.25 2.25"
            + " 0 0 0-1.883-2.542m-16.5 0V6A2.25 2.25 0 0 1 6 3.75h3.879a1.5 1.5 0 0 1 1.06.44l2.122 2.12a1.5 1"
            + ".5 0 0 0 1.06.44H18A2.25 2.25 0 0 1 20.25 9v.776"),

        UiIconName.Gift => SvgPath.D(
            "M20.625 11.505v8.25a1.5 1.5 0 0 1-1.5 1.5H4.875a1.5 1.5 0 0 1-1.5-1.5v-8.25m8.25-6.375A2.625 2.6"
            + "25 0 1 0 9 7.755h2.625m0-2.625v2.625m0-2.625a2.625 2.625 0 1 1 2.625 2.625h-2.625m0 0v13.5M3 11."
            + "505h18c.621 0 1.125-.504 1.125-1.125v-1.5c0-.622-.504-1.125-1.125-1.125H3c-.621 0-1.125.503-1.12"
            + "5 1.125v1.5c0 .621.504 1.125 1.125 1.125Z"),

        UiIconName.CodeBracket => SvgPath.D("M17.25 6.75 22.5 12l-5.25 5.25m-10.5 0L1.5 12l5.25-5.25m7.5-3-4.5 16.5"),

        UiIconName.Grip => SvgPath.D(
            "M12 6.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5ZM12 12.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5ZM12"
            + " 18.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5Z"),

        UiIconName.Cursor => SvgPath.D(
            "M15.042 21.672 13.684 16.6m0 0-2.51 2.225.569-9.47 5.227 7.917-3.286-.672ZM12 2.25V4.5m5.834.166"
            + "-1.591 1.591M20.25 10.5H18M7.757 14.743l-1.59 1.59M6 10.5H3.75m4.007-4.243-1.59-1.59"),

        UiIconName.Home => SvgPath.D(
            "m2.25 12 8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125"
            + " 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 "
            + "0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25"),

        UiIconName.Info => SvgPath.D(
            "m11.25 11.25.041-.02a.75.75 0 0 1 1.063.852l-.708 2.836a.75.75 0 0 0 1.063.853l.041-.021M21 12a9"
            + " 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9-3.75h.008v.008H12V8.25Z"),

        UiIconName.Moon => SvgPath.D(
            "M21.752 15.002A9.72 9.72 0 0 1 18 15.75c-5.385 0-9.75-4.365-9.75-9.75 0-1.33.266-2.597.748-3.752"
            + "A9.753 9.753 0 0 0 3 11.25C3 16.635 7.365 21 12.75 21a9.753 9.753 0 0 0 9.002-5.998Z"),

        UiIconName.Pencil => SvgPath.D(
            "m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L6.832 19.82a4.5 4.5 0 0 1-1.897 1.13l-2."
            + "685.8.8-2.685a4.5 4.5 0 0 1 1.13-1.897L16.863 4.487Zm0 0L19.5 7.125"),

        UiIconName.UserPlus => SvgPath.D(
            "M18 7.5v3m0 0v3m0-3h3m-3 0h-3m-2.25-4.125a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0ZM3 1"
            + "9.235v-.11a6.375 6.375 0 0 1 12.75 0v.109A12.318 12.318 0 0 1 9.374 21c-2.331 0-4.512-.645-6.374"
            + "-1.766Z"),

        UiIconName.Plus => SvgPath.D("M12 4.5v15m7.5-7.5h-15"),

        UiIconName.Save => SvgPath.D(
            "M9 8.25H7.5a2.25 2.25 0 0 0-2.25 2.25v9a2.25 2.25 0 0 0 2.25 2.25h9a2.25 2.25 0 0 0 2.25-2.25v-9"
            + "a2.25 2.25 0 0 0-2.25-2.25H15M9 12l3 3m0 0 3-3m-3 3V2.25"),

        UiIconName.Stop => SvgPath.D("M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"),

        UiIconName.Ticket => SvgPath.D(
            "M16.5 6v.75m0 3v.75m0 3v.75m0 3V18m-9-5.25h5.25M7.5 15h3M3.375 5.25c-.621 0-1.125.504-1.125 1.12"
            + "5v3.026a2.999 2.999 0 0 1 0 5.198v3.026c0 .621.504 1.125 1.125 1.125h17.25c.621 0 1.125-.504 1.1"
            + "25-1.125v-3.026a2.999 2.999 0 0 1 0-5.198V6.375c0-.621-.504-1.125-1.125-1.125H3.375Z"),

        UiIconName.Unlock => SvgPath.D(
            "M13.5 10.5V6.75a4.5 4.5 0 1 1 9 0v3.75M3.75 21.75h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25"
            + " 0 0 0-2.25-2.25H3.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z"),

        UiIconName.ChevronDown => SvgPath.D("m19.5 8.25-7.5 7.5-7.5-7.5"),

        UiIconName.ChevronUp => SvgPath.D("m4.5 15.75 7.5-7.5 7.5 7.5"),

        UiIconName.Fullscreen => SvgPath.D(
            "M3.75 3.75v4.5m0-4.5h4.5m-4.5 0L9 9M3.75 20.25v-4.5m0 4.5h4.5m-4.5 0L9 15M20.25 3.75h-4.5m4."
            + "5 0v4.5m0-4.5L15 9m5.25 11.25h-4.5m4.5 0v-4.5m0 4.5L15 15"),

        UiIconName.Key => SvgPath.D(
            "M15.75 5.25a3 3 0 0 1 3 3m3 0a6 6 0 0 1-7.029 5.912c-.563-.097-1.159.026-1.563.43L10.5 17.25"
            + "H8.25v2.25H6v2.25H2.25v-2.818c0-.597.237-1.17.659-1.591l6.499-6.499c.404-.404.527-1 .43-1."
            + "563A6 6 0 1 1 21.75 8.25Z"),

        // Unreachable for a declared name. A new enum member without a shape would otherwise draw an empty
        // box and read as a styling fault rather than a missing case.
        _ => throw new ArgumentOutOfRangeException(nameof(Name), Name, "No shape is defined for this icon."),
    };
}
