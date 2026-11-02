namespace Rask;

/// <summary>
/// A line between runs of links in a <see cref="UiNavmenu" />. Flux UI's <c>flux:navmenu.separator</c>.
/// </summary>
/// <remarks>
/// Drawn as a menu's separator is (measured on Flux's profile page: edge to edge across the panel's padding, 1px,
/// 5px above and below), under the navmenu's own marker.
/// </remarks>
public sealed partial class UiNavmenuSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() => UiMenuSeparator.Ruled(Div.Class(UiMenuSeparator.Box).Data("ui-navmenu-separator", ""));
}
