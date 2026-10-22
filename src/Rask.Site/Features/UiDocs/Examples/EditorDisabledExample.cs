namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.Editor.Value("<p>This one is read-only.</p>").Disabled();
}
