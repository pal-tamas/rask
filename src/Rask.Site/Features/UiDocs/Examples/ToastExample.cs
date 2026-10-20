namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Primary.Id("toast-plain").OnClick(() => Toast.Info("Your changes have been saved."))["Save changes"];
}
