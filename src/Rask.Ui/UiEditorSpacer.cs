namespace Rask;

/// <summary>
///     Pushes the toolbar items after it to the far edge: <c>~</c> in a toolbar's item list, Flux's
///     <c>flux:editor.spacer</c>.
/// </summary>
public sealed partial class UiEditorSpacer : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Div.Class("flex-1").Role("none");
}
