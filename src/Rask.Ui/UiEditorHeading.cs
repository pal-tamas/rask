namespace Rask;

/// <summary>
///     The heading level selector: the <c>heading</c> item of an editor's toolbar, Flux's
///     <c>flux:editor.heading</c>. Text, or a heading of level one to three.
/// </summary>
public sealed partial class UiEditorHeading : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Select(
            "heading",
            RaskStrings.Get(RaskString.EditorHeading, "Styles"),
            labelled: true,
            ("paragraph", RaskStrings.Get(RaskString.EditorHeadingText, "Text"), UiEditorIcons.Paragraph),
            ("heading1", RaskStrings.Get(RaskString.EditorHeading1, "Heading 1"), () => UiEditorIcons.Hero(Ui.IconName.H1)),
            ("heading2", RaskStrings.Get(RaskString.EditorHeading2, "Heading 2"), () => UiEditorIcons.Hero(Ui.IconName.H2)),
            ("heading3", RaskStrings.Get(RaskString.EditorHeading3, "Heading 3"), () => UiEditorIcons.Hero(Ui.IconName.H3)));
}
