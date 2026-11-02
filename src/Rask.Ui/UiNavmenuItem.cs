using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// One row of a <see cref="UiNavmenu" />. Flux UI's <c>flux:navmenu.item</c>.
/// </summary>
/// <remarks>
/// With an <see cref="Href" /> it is an ordinary link, drawn as a menu's row is: a generated route navigates
/// inside the app, a string is a plain <c>&lt;a&gt;</c> written as given. Without one it is a
/// <c>&lt;button&gt;</c>, as Flux's is, and <see cref="OnClick" /> is what it does.
/// </remarks>
public sealed partial class UiNavmenuItem : Component
{
    /// <summary>Where the link leads. Without one the row is a button.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>Runs when the row is pressed. Not for a string <see cref="Href" />: that row is the browser's link.</summary>
    public Callback OnClick { get; set; }

    /// <summary>An icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>
    ///     True to start the words where a sibling's icon ends: the room of an icon, empty, in a row that has none. Flux's
    ///     <c>indent</c>, as its profile page writes it under a row with a check.
    /// </summary>
    public bool? Indent { get; set; }

    /// <summary><see cref="Ui.MenuItemVariant.Danger" /> for a destructive destination.</summary>
    public Ui.MenuItemVariant? Variant { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content = [Lead(), .. Children ?? []];
        var classes = UiClass.Compose(UiMenuRow.LinkClasses(Variant), Class);
        return Row(classes).Data("ui-navmenu-item", "")[content];
    }

    // What stands before the words: the icon, or the icon's room when the row asks for it.
    private Component? Lead()
    {
        if (Icon is { } icon)
        {
            return UiMenuRow.LinkIcon(icon);
        }

        return Indent == true ? UiMenuRow.LinkIndent() : null;
    }

    private Element Row(string classes)
    {
        if (Href is not { } href)
        {
            var button = Button.Type(ButtonType.Button).Class(classes);
            return OnClick.HasValue ? button.OnClick(OnClick) : button;
        }

        // A STRING is an ordinary link, written exactly as given with no path base added (#1070): only a
        // generated route is routed inside the app.
        // It carries no handler: the runtime keeps the click of an element that has one, and the browser
        // would never follow the link.
        if (href.PageType is null)
        {
            return A.Href(href.ToString()).Class(classes);
        }

        var link = NavLink.Href(href).ActiveClass("").Class(classes);
        return OnClick.HasValue ? link.OnClick(OnClick) : link;
    }
}
