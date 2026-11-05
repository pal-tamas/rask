namespace Rask.Site.Features.UiDocs.Examples;

// The row a list page opens with: the trail, its title and a line under it, and what the page can do at the end.
// On a page of its own the title is its h1; here, under this page's own title, Level(2).
public sealed partial class PageHeaderExample : Component
{
    protected override Component? Render() =>
        Ui.PageHeader.Heading("Suppliers").Level(2).Subheading("Everyone you buy fuel and parts from.")
            .Breadcrumbs(Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href(Routes.UiIntroductionPage())["Home"], Ui.BreadcrumbsItem["Suppliers"]])[
            Ui.Button.Key("export").Icon(Ui.IconName.ArrowDownTray)["Export"],
            Ui.Button.Key("new").Primary.Icon(Ui.IconName.Plus).Href(Routes.UiFormButtonsPage())["New supplier"]
        ];
}
