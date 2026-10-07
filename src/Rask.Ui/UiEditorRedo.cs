namespace Rask;

/// <summary>
///     Redoes the last undone action: the <c>redo</c> item of an editor's toolbar, Flux's <c>flux:editor.redo</c>.
/// </summary>
public sealed partial class UiEditorRedo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("redo", "Redo", "⌘+Shift+Z", UiEditorIcons.Redo());
}
