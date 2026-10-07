namespace Rask;

/// <summary>
///     Underline text formatting: the <c>underline</c> item of an editor's toolbar, Flux's <c>flux:editor.underline</c>.
/// </summary>
public sealed partial class UiEditorUnderline : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("underline", "Underline", "⌘U", UiEditorIcons.Hero(Ui.IconName.Underline));
}
