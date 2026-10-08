using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// One action in a <see cref="UiMenu" />. Flux UI's <c>flux:menu.item</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its children are its words. It is a <c>menuitem</c> the keyboard cursor can land on, and acts the Rask way:
/// <see cref="OnClick" /> runs a handler, <see cref="Href" /> follows a link. Picking it closes the menu unless
/// it, or the menu, says <see cref="KeepOpen" />.
/// </para>
/// <code>
/// Ui.MenuItem.Icon(Ui.IconName.PencilSquare).Kbd("⌘S").OnClick(Save)["Save"]
/// Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(Delete)["Delete"]
/// </code>
/// </remarks>
public sealed partial class UiMenuItem : Component
{
    /// <summary>An icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon at the end of the row.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>Which drawing of the icons to use. The 20px <see cref="Ui.IconVariant.Mini" /> unless this says otherwise.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>
    ///     A keyboard shortcut shown at the end of the row — <c>"⌘S"</c>. Display only: it teaches the shortcut,
    ///     the app still binds it.
    /// </summary>
    public string? Kbd { get; set; }

    /// <summary>Words at the end of the row — a count, a state.</summary>
    public string? Suffix { get; set; }

    /// <summary><see cref="Ui.MenuItemVariant.Danger" /> for a destructive action.</summary>
    public Ui.MenuItemVariant? Variant { get; set; }

    /// <summary>Shown but not pickable. The keyboard cursor steps over it.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Keeps the menu open after this item is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>Where the item leads. In-app navigation for a generated route; an ordinary link for a string.</summary>
    public RouteUrl? Href { get; set; }

    public Callback OnClick { get; set; }

    public string? Class { get; set; }

    // Registration happens in Render, so a cached render would drop out of the menu's cursor.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var level = Context.Get<UiMenuLevel>();
        var label = UiMenuRow.Label(Children);
        if (level?.Scope.AsOptions == true)
        {
            // Inside Ui.Command, which is still drawn the old way until it is rebuilt on Flux's own item.
            return UiCommandRows.Option(level, label, Icon, Kbd, Href, Disabled == true, OnClick, Class);
        }

        var disabled = Disabled == true;
        var ordinal = level?.Scope.Register(level.Parent, label, disabled, isSub: false) ?? -1;
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Icon is not null)
        {
            data["ui-menu-item-has-icon"] = "";
        }

        if (KeepOpen == true)
        {
            data["rask-keep-open"] = "";
        }

        Component?[] content =
        [
            Icon is { } icon ? UiMenuRow.Icon(icon, IconVariant) : UiMenuRow.Indent(),
            .. Children ?? [],
            Suffix is { } suffix ? Div.Class(UiMenuRow.Trailing)[suffix] : null,
            Kbd is { } kbd ? Div.Class(UiMenuRow.Trailing)[kbd] : null,
            IconTrailing is { } trailing ? UiMenuRow.IconTrailing(trailing, IconVariant) : null
        ];
        var classes = UiClass.Compose(UiMenuRow.Classes(Variant), Class);

        if (Href is { Path: not null } href && !disabled)
        {
            var link = NavLink.Href(href).ActiveClass("").Class(classes).OnClick(() => PickAsync(level, ordinal));
            return UiMenuRow.Decorate(link, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
        }

        var button = Button.Type(ButtonType.Button).Class(classes).Disabled(disabled);
        if (!disabled)
        {
            button = button.OnClick(() => PickAsync(level, ordinal));
        }

        return UiMenuRow.Decorate(button, level, ordinal, "menuitem", "ui-menu-item", aria, data)[content];
    }

    // The row a pointer picks is where the cursor is from then on, which matters in a menu that stays open.
    private async Task PickAsync(UiMenuLevel? level, int ordinal)
    {
        level?.Scope.MoveTo(ordinal);
        await OnClick.Invoke().ConfigureAwait(false);
    }
}
