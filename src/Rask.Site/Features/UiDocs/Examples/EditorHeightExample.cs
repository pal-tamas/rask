namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorHeightExample : Component
{
    protected override Component? Render() =>
        Ui.Editor.Class("**:data-[slot=content]:min-h-[100px]!");
}
