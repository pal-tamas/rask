namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BreadcrumbsSlashesExample : Component
{
    protected override Component? Render() =>
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Key("home").Href("#").Separator(Ui.IconName.Slash)["Home"],
            Ui.BreadcrumbsItem.Key("blog").Href("#").Separator(Ui.IconName.Slash)["Blog"],
            Ui.BreadcrumbsItem.Key("post").Separator(Ui.IconName.Slash)["Post"]
        ];
}
