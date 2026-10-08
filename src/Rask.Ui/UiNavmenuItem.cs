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

    /// <summary>Runs when the row is pressed.</summary>
    public Callback OnClick { get; set; }

    /// <summary>An icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of the icon to use. The 20px <see cref="Ui.IconVariant.Mini" /> unless this says otherwise.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary><see cref="Ui.MenuItemVariant.Danger" /> for a destructive destination.</summary>
    public Ui.MenuItemVariant? Variant { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content = [Icon is { } icon ? UiMenuRow.LinkIcon(icon, IconVariant) : null, .. Children ?? []];
        var classes = UiClass.Compose(UiMenuRow.LinkClasses(Variant), Class);
        return Row(classes).Data("ui-navmenu-item", "")[content];
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
        if (href.PageType is null)
        {
            var plain = A.Href(href.ToString()).Class(classes);
            return OnClick.HasValue ? plain.OnClick(OnClick) : plain;
        }

        var link = NavLink.Href(href).ActiveClass("").Class(classes);
        return OnClick.HasValue ? link.OnClick(OnClick) : link;
    }
}
