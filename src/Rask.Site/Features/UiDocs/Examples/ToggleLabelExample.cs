namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleLabelExample : Component
{
    protected override Component? Render() =>
        Ui.Toggle.Icon(Ui.IconName.Bolt).Label("Fast mode");
}
