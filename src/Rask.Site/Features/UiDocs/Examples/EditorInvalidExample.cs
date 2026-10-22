namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorInvalidExample : Component
{
    protected override Component? Render() =>
        Ui.Editor.Placeholder("Write something...").Invalid();
}
