namespace Rask.Ui;

/// <summary>
/// A menu that opens where the reader right-clicks, over whatever <see cref="Target" /> is.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>context</c>. The children are the menu — <see cref="UiMenuItem" />, <see cref="UiMenuCheckbox" />,
/// <see cref="UiMenuSub" />, the same rows a <see cref="UiDropdown" /> takes — and it is the same
/// <see cref="UiMenuSurface" />, so the arrows, Home/End, type-ahead, submenus, Enter and Escape all behave as they
/// do there. Only the opening differs.
/// </para>
/// <para>
/// The opening is the RUNTIME's, not a round trip: a right-click on the target shows the popover at the pointer
/// straight away, on either host, and C# hears about it through the popover's toggle like any other menu. The
/// ContextMenu key and Shift+F10 open it too, at the focused element — so give the target something focusable
/// if the menu holds anything a keyboard user needs, since a right-click is otherwise the only way in.
/// </para>
/// <para>
/// Nothing in it should be the only way to do something. iOS Safari never fires a context-menu event and a
/// long-press elsewhere is the platform's text selection, so a context menu is a shortcut to actions that also
/// live somewhere visible.
/// </para>
/// </remarks>
public sealed partial class UiContextMenu : UiMenuSurface
{
    // The runtime writes the pointer position to these on <html> when it opens the menu — not on the panel or
    // this component's root, because a render rewrites (or removes) their style attributes and would throw the
    // position away the moment the cursor moved. One menu is open at a time, so one pair is enough.
    private const string PanelStyle =
        "position:fixed;inset:auto;left:var(--rask-context-x,0px);top:var(--rask-context-y,0px);margin:0;"
        + "overflow:visible";

    /// <summary>What the reader right-clicks — a card, a row, a canvas.</summary>
    public required Component Target { get; set; }

    /// <inheritdoc />
    private protected override string PrefixTag => "uicm";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var root = Div
            .Data("rask-contextmenu", PanelId)
            .Class(Class);
        if (IsOpen)
        {
            // Flux's `data-open`: a target that looks selected while its menu is up.
            root = root.Data("open", "");
        }

        return root[
            Target,
            MenuPanel(PanelStyle, null)
        ];
    }
}
