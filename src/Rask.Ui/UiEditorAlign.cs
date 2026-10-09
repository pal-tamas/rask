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
            RaskStrings.Get(RaskString.EditorAlign, "Align"),
            labelled: false,
            ("left", RaskStrings.Get(RaskString.EditorAlignLeft, "Left"), () => UiEditorIcons.Hero(Ui.IconName.Bars3BottomLeft)),
            ("center", RaskStrings.Get(RaskString.EditorAlignCenter, "Center"), () => UiEditorIcons.Hero(Ui.IconName.Bars3)),
            ("right", RaskStrings.Get(RaskString.EditorAlignRight, "Right"), () => UiEditorIcons.Hero(Ui.IconName.Bars3BottomRight)));
}
