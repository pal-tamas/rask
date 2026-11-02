namespace Rask;

/// <summary>
/// Empty space that grows, pushing what comes after it to the far end of a row or a column.
/// </summary>
/// <remarks>
/// Flux UI's <c>flux:spacer</c>. In a top bar, <c>[Ui.Brand, Ui.Spacer, Ui.Avatar]</c> puts the brand at the start
/// and the avatar at the end; in a sidebar it pushes the settings links to the bottom. It is <c>flex: 1</c> and
/// nothing else, so it only does anything inside a flex container. Measured on Flux's pages (2026-11-02): an empty
/// <c>&lt;div class="flex-1"&gt;</c> with its marker and no ARIA, which an empty element needs none of.
/// </remarks>
public sealed partial class UiSpacer : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Div.Class("flex-1").Data("ui-spacer", "");
}
