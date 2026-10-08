namespace Rask;

/// <summary>
///     Highlighted text: the <c>highlight</c> item of an editor's toolbar, Flux's <c>flux:editor.highlight</c>.
/// </summary>
public sealed partial class UiEditorHighlight : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("highlight", "Highlight", null, UiEditorIcons.Highlight());
}
