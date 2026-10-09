namespace Rask;

/// <summary>
///     Strikethrough text formatting: the <c>strike</c> item of an editor's toolbar, Flux's <c>flux:editor.strike</c>.
/// </summary>
public sealed partial class UiEditorStrike : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("strike", RaskStrings.Get(RaskString.EditorStrike, "Strikethrough"), "⌘+Shift+S", UiEditorIcons.Hero(Ui.IconName.Strikethrough));
}
