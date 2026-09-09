using Rask.Core.Components;

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
