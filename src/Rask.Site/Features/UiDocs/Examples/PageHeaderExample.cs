namespace Rask.Site.Features.UiDocs.Examples;

// A list page: the trail, its title and a line under it, what the page can do at the end of the row, then the list.
// The header keeps no room around itself; the page puts 24px under it.
public sealed partial class PageHeaderExample : Component
{
    protected override Component? Render() =>
        Div.Class("p-6 lg:p-8")[
            Ui.PageHeader.Heading("Suppliers").Subheading("Everyone you buy fuel and parts from.").Class("mb-6")
                .Breadcrumbs(Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href(Routes.UiIntroductionPage())["Home"], Ui.BreadcrumbsItem["Suppliers"]])[
                Ui.Button.Key("export").Icon(Ui.IconName.ArrowDownTray)["Export"],
                Ui.Button.Key("new").Primary.Icon(Ui.IconName.Plus).Href(Routes.UiFormButtonsPage())["New supplier"]
            ],
            Ui.Table.ContainerClass("min-w-0")[
                Ui.TableColumns[Ui.TableColumn["Supplier"], Ui.TableColumn["City"]],
                Ui.TableRows[
                    Ui.TableRow.Key("aminoff")[Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["Aminoff Freight"], Ui.TableCell["Oslo"]],
                    Ui.TableRow.Key("bothman")[Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["Bothman & Sons"], Ui.TableCell["Leeds"]]
                ]
            ]
        ];
}
