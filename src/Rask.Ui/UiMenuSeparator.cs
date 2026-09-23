namespace Rask;

/// <summary>
/// A line between groups of items.
/// </summary>
/// <remarks>
/// A <c>separator</c> to assistive tech, and not an item: the keyboard cursor never lands on it.
/// </remarks>
public sealed partial class UiMenuSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        // In a command palette being searched, what it separated has been filtered, so a line would divide nothing.
        Context.Get<UiMenuLevel>()?.Scope.Filtering == true
            ? null
            : Li.Role("separator").Class("ui-menu-separator");
}
