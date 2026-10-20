namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BreadcrumbsIconExample : Component
{
    protected override Component? Render() =>
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Key("home").Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem.Key("blog").Href("#")["Blog"],
            Ui.BreadcrumbsItem.Key("post")["Post"]
        ];
}
