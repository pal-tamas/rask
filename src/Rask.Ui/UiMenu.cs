using System.Globalization;

namespace Rask;

/// <summary>
/// A menu of actions, with the keyboard cursor. Flux UI's <c>flux:menu</c>.
/// </summary>
/// <remarks>
/// <para>
/// It is the popup itself: a <c>[popover]</c> that says <c>role="menu"</c>, opened by the
/// <see cref="UiDropdown" /> or the <see cref="UiContext" /> it sits in. Its children are the rows —
/// <see cref="UiMenuItem" />, <see cref="UiMenuCheckbox" />, <see cref="UiMenuRadio" />,
/// <see cref="UiMenuSubmenu" />, <see cref="UiMenuSeparator" />, <see cref="UiMenuGroup" />.
/// </para>
/// <para>
/// The keyboard is Flux's, key for key. It opens with focus on the menu and no row chosen; ArrowDown or ArrowUp
/// puts the cursor on the first row — or on the row the pointer is resting on — and from there the arrows move
/// it a row at a time, stopping at the ends and stepping over what is disabled. Typing letters jumps to the row
/// that starts with them. ArrowRight or Enter on a submenu's row opens it with the cursor on its first row,
/// ArrowLeft closes it. Enter or Space picks, Tab leaves, Escape closes — and the row under the cursor is the
/// one that has FOCUS, so a screen reader follows it.
/// </para>
/// <para>
/// The pointer is Flux's too. A row is lit — <c>data-active</c>, the one mark both the pointer and the keyboard
/// write — the moment the pointer enters it, and focus stays where it was; the arrows then count from the lit
/// row. A submenu opens under the pointer, stays while the pointer crosses to it and after it has left the menu,
/// and closes when another row is entered. The page behind neither scrolls nor takes the pointer — except behind
/// a menu the pointer opened, which locks nothing and takes no focus.
/// </para>
/// <para>
/// <b>None of that is the page's state.</b> Where the reader is in an open menu — the lit row, the focused one,
/// the open flyouts — is kept in the browser by the runtime's hooks (<c>data-rask-menu-cursor</c>,
/// <c>data-rask-menu-pointer</c>, <c>data-rask-safe-area</c>, <c>data-rask-lock</c>), as Flux keeps it in script:
/// a pointer gliding down the menu and an arrow key held down send the page nothing. The page hears a row when
/// it is pressed, and the menu opening and closing.
/// </para>
/// <para>
/// A pick closes the menu. <see cref="KeepOpen" /> keeps it up, for a menu of checkboxes; a row can ask for the
/// same on its own.
/// </para>
/// </remarks>
public sealed partial class UiMenu : Component
{
    // Per instance: a menu outside a dropdown still needs an id of its own.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private UiPopupHost? _host;

    /// <summary>Keeps the menu open after any row in it is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>
    ///     The menu's id, for one that sits outside the <see cref="UiContext" /> that opens it and is named by its
    ///     <see cref="UiContext.Target" />. Inside a dropdown or a context menu the id is theirs.
    /// </summary>
    public string? Id { get; set; }

    public string? Class { get; set; }

    // The rows take their ids from a scope made here, each render.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        _host = Context.Get<UiPopupHost>();
        var id = _host?.PanelId ?? Id ?? "uimenu-" + _instance.ToString(CultureInfo.InvariantCulture);

        // The runtime keeps the cursor (rask-menu-keys.ts) and lights the row under the pointer (rask-menu.ts).
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-menu"] = "",
            ["rask-menu-cursor"] = "",
            ["rask-menu-pointer"] = "",
        };
        if (_host?.Hover != true)
        {
            // The page behind neither scrolls nor takes the pointer, as under Flux's dropdown and context menu
            // (rask-lock.ts). Not under a menu the pointer opens: a page with no pointer would take it from the
            // trigger too, and Flux locks nothing there either.
            data["rask-lock"] = "";
        }

        if (KeepOpen == true)
        {
            // The runtime closes the popover on a pick unless the row, or something around it, says this.
            data["rask-keep-open"] = "";
        }

        if (_host?.Controlled is { } controlled)
        {
            // The runtime shows or hides the popover to match whenever this changes (rask-dom.ts).
            data["rask-popover-open"] = controlled ? "true" : "false";
        }

        if (_host?.Detail is { } detail)
        {
            data["detail"] = detail;
        }

        // `autofocus`: the popover focusing steps move focus to it as it opens, so the arrow keys reach the menu
        // straight from whatever opened it — with no row chosen yet, as Flux leaves it.
        // On its own it is a menu some Ui.Context names by id, so it sits where the pointer was.
        var style = _host?.Style ?? global::Rask.UiContext.PointerStyle(Ui.ContextPosition.BottomEnd, distance: null, (0, 0));

        return Div
            .Id(id)
            .Popover(Popover.Auto)
            .Role("menu")
            .TabIndex(-1)
            .Class(UiClass.Compose(UiMenuRow.Panel, Class))
            .Data(data)
            .Attributes(("autofocus", null), ("style", style))
            // The SOLE writer of the open state: the browser opens the popover and closes it on Escape, a click
            // outside, a pick and Tab, and this is where C# hears which.
            .OnToggle(OnToggleAsync)[
            Context.Provide(new UiMenuLevel(new UiMenuScope(id), -1))[Children ?? []]
        ];
    }

    private Task OnToggleAsync(ToggleEvent e) =>
        _host is { } host
            ? host.Toggled(string.Equals(e.NewState, "open", StringComparison.Ordinal))
            : Task.CompletedTask;
}
