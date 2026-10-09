namespace Rask;

/// <summary>
///     Code formatting: the <c>code</c> item of an editor's toolbar, Flux's <c>flux:editor.code</c>.
/// </summary>
public sealed partial class UiEditorCode : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("code", RaskStrings.Get(RaskString.EditorCode, "Code"), null, UiEditorIcons.Code());
}
