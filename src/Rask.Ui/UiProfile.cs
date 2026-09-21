namespace Rask.Ui;

/// <summary>
/// The account row at the foot of a <see cref="UiSidebar" />: who is signed in, and the menu of what they can
/// do about it.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>profile</c>. Give it children and the row IS the menu button — the same
/// <see cref="UiMenuButton" /> contract <see cref="UiDropdown" /> uses, so the browser owns open, Escape,
/// click-outside and focus return, and the arrows, Home/End, type-ahead and Enter all work. Give it none and it
/// is a plain row, for a surface where the account is shown rather than acted on.
/// </para>
/// <para>
/// <see cref="Avatar" /> is optional. Without one the row draws the INITIALS of <see cref="Name" />, which is
/// what makes this usable for a real signed-in person: most accounts have no picture, and a broken image is
/// worse than a monogram.
/// </para>
/// <para>
/// The menu opens UPWARD by default, because the row sits at the bottom of the sidebar and a menu below it
/// would open off the screen. <see cref="UiMenuButton.Position" /> says otherwise.
/// </para>
/// </remarks>
public sealed partial class UiProfile : UiMenuButton
{
    /// <summary>Who is signed in. Also the source of the initials, when there is no <see cref="Avatar" />.</summary>
    public required string Name { get; set; }

    /// <summary>The picture. Omitted, the row draws the initials of <see cref="Name" /> instead.</summary>
    public string? Avatar { get; set; }

    /// <summary>The line under the name — an email address, a role, the tenant.</summary>
    public string? Caption { get; set; }

    /// <summary>Squares the avatar. It is a circle unless this says otherwise.</summary>
    public bool? Circle { get; set; }

    /// <summary>Hides the chevron on a row that has a menu, for a row that says so some other way.</summary>
    public bool? Chevron { get; set; }

    /// <inheritdoc />
    private protected override string PrefixTag => "uipr";

    /// <inheritdoc />
    private protected override string TriggerClass =>
        "btn btn-ghost h-auto w-full justify-start gap-2 px-2 py-1.5 font-normal";

    /// <inheritdoc />
    private protected override string RootClass => "relative block w-full";

    /// <inheritdoc />
    private protected override UiPosition DefaultPosition => UiPosition.Top;

    /// <inheritdoc />
    protected override Component? Render() => Children is null ? Row(button: false) : MenuButton();

    /// <inheritdoc />
    private protected override Component TriggerContent() => Row(button: true);

    // `title` on the row: in the rail only the avatar is left, and the name moves into the tooltip — and, for the
    // dropdown trigger, into the button's accessible name, which a name computed from content takes from a
    // descendant's title once its text is hidden. See UiNavItem for why not a CSS tooltip.
    private Component Row(bool button) =>
        Div.Class(UiClass.Compose(
            "flex min-w-0 items-center gap-2",
            button ? "w-full" : "px-2 py-1.5",
            button ? null : Class)).Title(Name)[
            Face(),
            // ui-rail-hide: in a collapsable sidebar narrowed to its rail, the avatar is the whole row.
            Div.Class("ui-rail-hide flex min-w-0 grow flex-col text-start leading-tight")[
                Span.Class("truncate text-sm font-medium")[Name],
                Caption is { } caption ? Span.Class("truncate text-xs opacity-60")[caption] : null
            ],
            button && Chevron != false
                ? UiIcon.Name(UiIconName.ChevronDown).Class("ui-rail-hide size-4 shrink-0 opacity-60")
                : null
        ];

    // The picture, or the monogram standing in for it. daisyUI's `avatar-placeholder` is the documented shape
    // for the second — a <div> where the <img> would be, so the size and the rounding are the same rules either
    // way.
    private Component Face()
    {
        var shape = Circle == false ? "rounded" : "rounded-full";

        return Avatar is { Length: > 0 } src
            ? Div.Class("avatar shrink-0")[
                Div.Class(UiClass.Compose("size-8", shape))[
                    // The name is already beside it, so the picture adds nothing an assistive reader needs —
                    // announcing it twice is noise. An EMPTY alt is how you say "decorative"; a missing one
                    // makes a screen reader read the file name instead.
                    Img.Src(src).Alt("")
                ]
            ]
            : Div.Class("avatar avatar-placeholder shrink-0")[
                Div.Class(UiClass.Compose("size-8 bg-neutral text-neutral-content", shape))[
                    // Hidden, for the same reason: the name is right there, and "TP" read aloud before it is
                    // noise.
                    Span.Class("text-xs font-medium").Aria("hidden", "true")[global::Rask.Ui.UiAvatar.Initials(Name)]
                ]
            ];
    }
}
