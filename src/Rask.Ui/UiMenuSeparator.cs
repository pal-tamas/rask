namespace Rask.Ui;

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
        Li.Role("separator").Class("ui-menu-separator");
}
