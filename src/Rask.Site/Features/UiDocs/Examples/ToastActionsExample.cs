namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastActionsExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Id("toast-action")
            .OnClick(() => Toast.Success("Your updates are now live.").Heading("Changes saved")
                .Action("Undo", () => Toast.Info("Changes undone.")))["With an action"];
}
