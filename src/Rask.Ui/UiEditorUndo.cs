namespace Rask;

/// <summary>
///     Undoes the last action: the <c>undo</c> item of an editor's toolbar, Flux's <c>flux:editor.undo</c>.
/// </summary>
public sealed partial class UiEditorUndo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("undo", RaskStrings.Get(RaskString.EditorUndo, "Undo"), "⌘Z", UiEditorIcons.Undo());
}
