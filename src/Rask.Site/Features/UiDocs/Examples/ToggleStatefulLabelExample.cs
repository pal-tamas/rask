namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleStatefulLabelExample : Component
{
    protected override Component? Render() =>
        Ui.Toggle.OnLabel("Sound on").OffLabel("Sound off");
}
