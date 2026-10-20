namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BreadcrumbsExample : Component
{
    protected override Component? Render() =>
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Key("home").Href("#")["Home"],
            Ui.BreadcrumbsItem.Key("blog").Href("#")["Blog"],
            Ui.BreadcrumbsItem.Key("post")["Post"]
        ];
}
