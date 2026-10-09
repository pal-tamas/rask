namespace Rask;

/// <summary>
///     A numbered list: the <c>ordered</c> item of an editor's toolbar, Flux's <c>flux:editor.ordered</c>.
/// </summary>
public sealed partial class UiEditorOrdered : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("ordered", RaskStrings.Get(RaskString.EditorOrdered, "Ordered list"), null, UiEditorIcons.Ordered());
}
