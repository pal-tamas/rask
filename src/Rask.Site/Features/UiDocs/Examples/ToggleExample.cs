namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleExample : Component
{
    protected override Component? Render() =>
        Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode");
}
