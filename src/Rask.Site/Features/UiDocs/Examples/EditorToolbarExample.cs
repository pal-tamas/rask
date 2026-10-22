namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorToolbarExample : Component
{
    protected override Component? Render() =>
        Ui.Editor.Toolbar("heading | bold italic underline | align ~ undo redo").Placeholder("Write something...");
}
