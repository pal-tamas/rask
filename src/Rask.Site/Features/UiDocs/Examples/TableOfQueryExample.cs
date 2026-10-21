using System.Globalization;
using Rask.Querying;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableOfQueryExample : Component
{
    private const int PageSize = 5;
    private const int Count = 40;

    private static readonly string[] Customers =
    [
        "Lindsey Aminoff", "Hanna Lubin", "Kianna Bushevi", "Gustavo Geidt", "Nolan George",
        "Desirae George", "Jackson Bothman", "Hanna Lipshutz", "Alfredo Levin", "Zain Lubin",
    ];

    private int _page = 1;
    private bool _newestFirst;
    private string _status = "open";

    // One query for the table: its input is the page, the order and the tab, and every change of them loads.
    private Query<Invoice[]> Invoices => field ??= QueryClient.Query(
        QueryKey.Of("ui-table-of-query"), () => (_page, _newestFirst, _status), (asked, ct) => Read(asked, ct));

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Ui.Tabs.Value(_status).OnChange(status => { _status = status; _page = 1; })[
                Ui.Tab.Name("open")["Open"],
                Ui.Tab.Name("paid")["Paid"]
            ],
            Ui.Table.Id("ui-invoices-of").Of(Invoices)
                .Class("w-full")
                .ContainerClass("min-w-0")
                .Paginate(Ui.Pagination
                    .Paginator(new UiPaginator { Page = _page, PerPage = PageSize, Total = Count })
                    .OnPage(page => _page = page))[
                Ui.TableColumns[
                    Ui.TableColumn.Class("w-32").Sortable().Sorted(true).Direction(Direction).OnSort(TurnRound)["Invoice"],
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn.End.Class("w-28")["Amount"]
                ],
                Ui.TableRows[
                    (Invoices.Data ?? []).Select(invoice => Ui.TableRow.Key(invoice.Number)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[$"INV-{invoice.Number:0000}"],
                        Ui.TableCell.Class("truncate")[invoice.Customer],
                        Ui.TableCell.End[invoice.Amount.ToString("C", CultureInfo.GetCultureInfo("en-US"))]
                    ])
                ]
            ]
        ];

    private Ui.TableColumnDirection Direction =>
        _newestFirst ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc;

    private void TurnRound()
    {
        _newestFirst = !_newestFirst;
        _page = 1;
    }

    // A slow read, so the skeleton has time to be seen.
    private static async Task<Invoice[]> Read((int Page, bool NewestFirst, string Status) asked, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
        var paid = asked.Status == "paid" ? 500 : 0;
        return
        [
            .. Enumerable.Range(((asked.Page - 1) * PageSize) + 1, PageSize)
                .Select(row => asked.NewestFirst ? Count + 1 - row : row)
                .Select(number => new Invoice(paid + number, Customers[number % Customers.Length], 20m + (number * 7.5m))),
        ];
    }

    private sealed record Invoice(int Number, string Customer, decimal Amount);
}
