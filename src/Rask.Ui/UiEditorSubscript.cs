namespace Rask;

/// <summary>
///     Subscript text formatting: the <c>subscript</c> item of an editor's toolbar, Flux's <c>flux:editor.subscript</c>.
/// </summary>
public sealed partial class UiEditorSubscript : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("subscript", RaskStrings.Get(RaskString.EditorSubscript, "Subscript"), null, UiEditorIcons.Subscript());
}
