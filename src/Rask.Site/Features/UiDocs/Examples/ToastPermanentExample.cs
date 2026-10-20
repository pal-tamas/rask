namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastPermanentExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Id("toast-permanent")
            .OnClick(() => Toast.Info("Your changes have been saved.").UntilDismissed())["Permanent"];
}
