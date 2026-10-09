namespace Rask;

/// <summary>
///     Italic text formatting: the <c>italic</c> item of an editor's toolbar, Flux's <c>flux:editor.italic</c>.
/// </summary>
public sealed partial class UiEditorItalic : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("italic", RaskStrings.Get(RaskString.EditorItalic, "Italic"), "⌘I", UiEditorIcons.Hero(Ui.IconName.Italic));
}
