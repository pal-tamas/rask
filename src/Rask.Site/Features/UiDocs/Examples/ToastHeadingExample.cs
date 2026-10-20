namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastHeadingExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Id("toast-heading")
            .OnClick(() => Toast.Info("You can always update this in your settings.").Heading("Changes saved"))["With heading"];
}
