namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastDurationExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Id("toast-brief")
            .OnClick(() => Toast.Info("Your changes have been saved.").For(1.Second))["One second"];
}
