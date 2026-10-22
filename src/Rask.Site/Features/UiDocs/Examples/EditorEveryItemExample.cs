namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorEveryItemExample : Component
{
    protected override Component? Render() =>
        Ui.Editor
            .Toolbar("heading | bold italic strike underline | subscript superscript highlight code | bullet ordered blockquote | link | align ~ undo redo")
            .Placeholder("Write something...");
}
