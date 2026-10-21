using System.Globalization;
using Rask.Core;

namespace Rask.Printing.Tests.Host;

// An app page drawn with the kit and nothing else: the sidebar, the header, buttons, a closable badge, an accordion with
// a closed item and a table over several pages with its pager — on the kit's compiled sheet, as a page links it.
public sealed partial class KitPageSheet : Component
{
    protected override Component? Render() =>
        Div[
            Style[Raw.Value(UiStylesheet.Css)],
            Style[Raw.Value("body { font-family: Inter, sans-serif; margin: 0; }")],
            Div.Class("min-h-screen")[
                Ui.Sidebar[Ui.SidebarNav[Ui.SidebarItem.Href("#")["Oldalsáv menüpont"]]],
                Ui.Header[Ui.SidebarToggle, Ui.Text["Fejléc felirat"]],
                Ui.Main[
                    Ui.Heading.Level(1)["Visszatérítések"],
                    Div.Class("flex gap-2")[Ui.Button.Primary["Mentés gomb"], Ui.Badge["Admin", Ui.BadgeClose]],
                    Ui.Accordion.Class("my-4")[
                        Ui.AccordionItem.Heading("Nyitott tétel").Expanded()["Nyitott tartalom."],
                        Ui.AccordionItem.Heading("Zárt tétel")["Zárt tartalom, nyomtatva látszik."]
                    ],
                    Ui.Table.Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 90, Total = 300 }))[
                        Ui.TableColumns[Ui.TableColumn["Számlaszám"], Ui.TableColumn["Megjegyzés"], Ui.TableColumn.Align(Ui.Align.End)["Összeg"]],
                        Ui.TableRows[Enumerable.Range(1, 90).Select(Row)]
                    ]
                ]
            ]
        ];

    private static Component Row(int number) => Ui.TableRow.Key(number)[
        Ui.TableCell[$"SZ-{number:D5}"],
        Ui.TableCell[number % 10 == 0
            ? "Hosszú megjegyzés, amely a képernyőn egy sorban marad és levágódna, papíron viszont tördelve kifér a hasábba."
            : "Rendben"],
        Ui.TableCell.Align(Ui.Align.End)[(number * 1250).ToString("N0", CultureInfo.InvariantCulture) + " Ft"]
    ];
}
