namespace Rask;

/// <summary>
///     Superscript text formatting: the <c>superscript</c> item of an editor's toolbar, Flux's <c>flux:editor.superscript</c>.
/// </summary>
public sealed partial class UiEditorSuperscript : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("superscript", RaskStrings.Get(RaskString.EditorSuperscript, "Superscript"), null, UiEditorIcons.Superscript());
}
