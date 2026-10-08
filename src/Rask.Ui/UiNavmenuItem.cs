using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// One link in a <see cref="UiNavmenu" />. Flux UI's <c>flux:navmenu.item</c>.
/// </summary>
/// <remarks>
/// An ordinary link, drawn as a menu's row is. A generated route navigates inside the app; a string is a plain
/// link.
/// </remarks>
public sealed partial class UiNavmenuItem : Component
{
    /// <summary>Where the link leads.</summary>
    public required RouteUrl Href { get; set; }

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
        return NavLink
            .Href(Href)
            .ActiveClass("")
            .Class(UiClass.Compose(UiMenuRow.LinkClasses(Variant), Class))
            .Data("ui-navmenu-item", "")[content];
    }
}
