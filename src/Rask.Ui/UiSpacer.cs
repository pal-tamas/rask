namespace Rask.Ui;

/// <summary>
/// Empty space that grows, pushing what comes after it to the far end of a row or a column.
/// </summary>
/// <remarks>
/// Flux UI's spacer. In a top bar, <c>[UiBrand, UiSpacer, UiThemeDropdown]</c> puts the brand at the start and the
/// picker at the end; in a sidebar it pushes the settings links to the bottom. It is <c>flex: 1</c> and nothing
/// else, so it only does anything inside a flex container, and it is hidden from assistive tech because there is
/// nothing in it.
/// </remarks>
public sealed partial class UiSpacer : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex-1").Aria("hidden", "true");
}
