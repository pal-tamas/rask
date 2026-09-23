namespace Rask;

/// <summary>
/// A button that opens a menu beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A popover menu, keyboard and all.</b> The panel is a <c>[popover]</c>, so the browser owns opening and
/// closing it: the top layer, so no <c>overflow: hidden</c> ancestor clips it; Escape and a click outside to
/// dismiss it; and focus handed back to the trigger when it closes. C# owns only the keyboard cursor, the way
/// Flux UI's menus behave: the arrow keys move it, Home and End jump, typing a letter jumps to the next item that
/// starts with it, ArrowRight opens a submenu and ArrowLeft closes it, Enter or Space picks, Tab leaves. The cursor
/// is <c>aria-activedescendant</c> on the menu, so focus never leaves the list while it moves. All of that lives on
/// <see cref="UiMenuButton" />, which <see cref="UiProfile" /> shares.
/// </para>
/// <para>
/// Without a runtime — a prerendered page — the trigger still opens the menu and every item is still a link or a
/// button; the keyboard cursor is what arrives with the runtime.
/// </para>
/// <para>
/// <b><see cref="UiMenuSurface.Open" /> decides who owns the open state, and it has three settings rather than
/// two.</b> Unset, the dropdown is UNCONTROLLED: the reader opens and closes it and the page is not asked. Set to
/// <c>true</c> or <c>false</c> it is CONTROLLED — the runtime shows or hides the popover to match whenever the page
/// changes it, which is what lets a page close the menu when the action inside it completes, and
/// <see cref="UiMenuSurface.OnToggle" /> is how the page hears the reader open or close it.
/// </para>
/// <para>
/// <see cref="Ui.OpenOn.Hover" /> keeps daisyUI's CSS dropdown, because CSS cannot open a popover: it opens on a
/// pointer and on focus, and has no keyboard cursor.
/// </para>
/// </remarks>
public sealed partial class UiDropdown : UiMenuButton
{
    /// <summary>The label on the button that opens it.</summary>
    public required string Trigger { get; set; }

    /// <summary>
    ///     What opens it. <see cref="Ui.OpenOn.Hover" /> is daisyUI's CSS dropdown — pointer and focus, no keyboard
    ///     cursor — so prefer the default for anything that has to be reachable by keyboard or touch.
    /// </summary>
    public Ui.OpenOn? OpenOn { get; set; }

    /// <summary>An icon before the trigger's label.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon after the trigger's label. A chevron unless this says otherwise.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <inheritdoc />
    private protected override string PrefixTag => "uidd";

    /// <inheritdoc />
    private protected override string TriggerClass => "btn";

    /// <inheritdoc />
    protected override Component? Render() => OpenOn == Ui.OpenOn.Hover ? HoverDropdown() : MenuButton();

    /// <inheritdoc />
    private protected override Component TriggerContent() =>
    [
        Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null,
        Span[Trigger],
        Ui.Icon.Name(IconTrailing ?? Ui.IconName.ChevronDown).Class("size-4 shrink-0 opacity-60")
    ];

    // daisyUI's CSS dropdown, for Hover: it opens on :hover and :focus-within, which a popover cannot.
    private Component HoverDropdown() =>
        Div.Class(UiClass.Compose(
            "dropdown dropdown-hover",
            Position is { } position ? UiClassNames.DropdownPosition(position) : "",
            Align is { } align ? UiClassNames.DropdownAlign(align) : "",
            Class))[
            // tabindex so :focus-within can open it from the keyboard; daisyUI scopes its rules to it.
            Button.Type("button").Class("btn").TabIndex(0).Aria(new Dictionary<string, string?> { ["haspopup"] = "menu" })[
                Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null,
                Span[Trigger],
                Ui.Icon.Name(IconTrailing ?? Ui.IconName.ChevronDown).Class("size-4 shrink-0 opacity-60")
            ],
            Ul.Class("dropdown-content menu z-1 w-56 rounded-box bg-base-100 p-2 shadow-sm")[Children ?? []]
        ];
}
