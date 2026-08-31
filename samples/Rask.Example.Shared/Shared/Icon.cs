namespace Rask.Example.Shared;

/// <summary>The icons the showcase actually uses.</summary>
/// <remarks>
/// A closed enum rather than a string, for the reason the typed <c>BsIconName</c> it replaces existed:
/// a mistyped icon name renders nothing and reports nothing, so it is worth a compile error.
/// </remarks>
public enum IconName
{
    /// <summary>Fallback — a neutral dot.</summary>
    None,

    Airplane,
    ArrowClockwise,
    ArrowCounterclockwise,
    ArrowDownUp,
    ArrowLeft,
    ArrowRepeat,
    ArrowRight,
    ArrowUp,
    BagCheck,
    Bug,
    CalendarCheck,
    Check2Circle,
    CheckCircle,
    ChevronDown,
    ChevronRight,
    CircleHalf,
    Clipboard,
    CreditCard,
    DashLg,
    ExclamationOctagonFill,
    ExclamationTriangle,
    FileEarmarkText,
    Gift,
    Github,
    GripVertical,
    HandIndex,
    House,
    InfoCircle,
    JournalText,
    List,
    MoonStars,
    Pencil,
    PersonPlus,
    PlayCircle,
    PlayFill,
    Plus,
    PlusLg,
    Save,
    Search,
    Send,
    ShieldCheck,
    ShieldExclamation,
    StopCircle,
    TicketPerforated,
    Trash,
    Unlock,
    XLg,
}

/// <summary>
/// A glyph, standing in for the icon font the showcase used to pull from Rask.Bootstrap.
/// </summary>
/// <remarks>
/// Unicode rather than inline SVG, deliberately. The showcase demonstrates the FRAMEWORK, and 45 hand-
/// transcribed SVG paths would be 45 chances to get path data subtly wrong in a way no test would
/// catch — a wrong glyph still renders. A character is legible, weightless, needs no asset to fetch,
/// and inherits colour and size from the utilities around it like any other text.
/// <para>
/// <c>aria-hidden</c> on every one: they sit beside a label at every call site in this showcase, so a
/// screen reader announcing them would only repeat what the label already says.
/// </para>
/// </remarks>
public sealed partial class Icon : Component
{
    /// <summary>Which glyph.</summary>
    public required IconName Name { get; set; }

    /// <summary>Extra utilities — size and colour come from here.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span
            .Class(Class is null ? "inline-block leading-none" : $"inline-block leading-none {Class}")
            .Attributes(("aria-hidden", "true"))[Glyph()];

    private string Glyph() => Name switch
    {
        IconName.Airplane => "✈",
        IconName.ArrowClockwise => "↻",
        IconName.ArrowCounterclockwise => "↺",
        IconName.ArrowDownUp => "⇅",
        IconName.ArrowLeft => "←",
        IconName.ArrowRepeat => "⟲",
        IconName.ArrowRight => "→",
        IconName.ArrowUp => "↑",
        IconName.BagCheck => "🛍",
        IconName.Bug => "🐞",
        IconName.CalendarCheck => "📅",
        IconName.Check2Circle => "✅",
        IconName.CheckCircle => "✓",
        IconName.ChevronDown => "▾",
        IconName.ChevronRight => "▸",
        IconName.CircleHalf => "◐",
        IconName.Clipboard => "📋",
        IconName.CreditCard => "💳",
        IconName.DashLg => "—",
        IconName.ExclamationOctagonFill => "⛔",
        IconName.ExclamationTriangle => "⚠",
        IconName.FileEarmarkText => "📄",
        IconName.Gift => "🎁",
        IconName.Github => "⌥",
        IconName.GripVertical => "⣿",
        IconName.HandIndex => "☞",
        IconName.House => "⌂",
        IconName.InfoCircle => "ⓘ",
        IconName.JournalText => "📓",
        IconName.List => "☰",
        IconName.MoonStars => "🌙",
        IconName.Pencil => "✎",
        IconName.PersonPlus => "👤",
        IconName.PlayCircle => "▶",
        IconName.PlayFill => "▶",
        IconName.Plus => "+",
        IconName.PlusLg => "+",
        IconName.Save => "💾",
        IconName.Search => "🔍",
        IconName.Send => "➤",
        IconName.ShieldCheck => "🛡",
        IconName.ShieldExclamation => "🛡",
        IconName.StopCircle => "■",
        IconName.TicketPerforated => "🎫",
        IconName.Trash => "🗑",
        IconName.Unlock => "🔓",
        IconName.XLg => "✕",
        _ => "•",
    };
}
