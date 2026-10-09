namespace Rask;

/// <summary>
///     Bold text formatting: the <c>bold</c> item of an editor's toolbar, Flux's <c>flux:editor.bold</c>.
/// </summary>
public sealed partial class UiEditorBold : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("bold", RaskStrings.Get(RaskString.EditorBold, "Bold"), "⌘B", UiEditorIcons.Hero(Ui.IconName.Bold));
}
