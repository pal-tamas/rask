namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastLinksExample : Component
{
    protected override Component? Render() =>
        Ui.Button.Id("toast-link")
            .OnClick(() => Toast.Success("Invoice created.")
                .Link("View invoice", PageMeta.LinkTo(Routes.UiKitDataDisplayPage())))["With a link"];
}
