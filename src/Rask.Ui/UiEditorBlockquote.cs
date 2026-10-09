namespace Rask;

/// <summary>
///     Block quote formatting: the <c>blockquote</c> item of an editor's toolbar, Flux's <c>flux:editor.blockquote</c>.
/// </summary>
public sealed partial class UiEditorBlockquote : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("blockquote", RaskStrings.Get(RaskString.EditorBlockquote, "Blockquote"), "⌘+Shift+B", UiEditorIcons.Blockquote());
}
