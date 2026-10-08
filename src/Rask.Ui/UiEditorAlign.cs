namespace Rask;

/// <summary>
///     Text alignment: the <c>align</c> item of an editor's toolbar, Flux's <c>flux:editor.align</c>.
///     Left, center or right.
/// </summary>
public sealed partial class UiEditorAlign : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Select(
            "align",
            "Align",
            labelled: false,
            ("left", "Left", () => UiEditorIcons.Hero(Ui.IconName.Bars3BottomLeft)),
            ("center", "Center", () => UiEditorIcons.Hero(Ui.IconName.Bars3)),
            ("right", "Right", () => UiEditorIcons.Hero(Ui.IconName.Bars3BottomRight)));
}
