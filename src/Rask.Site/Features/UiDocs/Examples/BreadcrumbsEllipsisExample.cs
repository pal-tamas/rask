namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BreadcrumbsEllipsisExample : Component
{
    protected override Component? Render() =>
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Key("home").Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem.Key("more").Icon(Ui.IconName.EllipsisHorizontal),
            Ui.BreadcrumbsItem.Key("post")["Post"]
        ];
}
