using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TablePagingThatWaitsExample : Component
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
    private bool _slow;
    private Invoice[] _invoices = Read(1, newestFirst: false);

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Div[
                Ui.Button.Sm.Data("testid", "ui-invoices-slow").Loading(false).OnClick(() => { _slow = !_slow; })[
                    _slow ? "The read takes 1.5 s" : "The read is instant"
                ]
            ],
            Ui.Table
                .Id("ui-invoices")
                .Class("w-full")
                .ContainerClass("min-w-0")
                .Paginate(Ui.Pagination
                    .Paginator(new UiPaginator { Page = _page, PerPage = PageSize, Total = Count })
                    .OnPage(Show))[
                Ui.TableColumns[
                    Ui.TableColumn.Sortable().Sorted(true).Direction(Direction).OnSort(TurnRound)["Invoice"],
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn.End["Amount"]
                ],
                Ui.TableRows[
                    _invoices.Select(invoice => Ui.TableRow.Key(invoice.Number)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[$"INV-{invoice.Number:0000}"],
                        Ui.TableCell[invoice.Customer],
                        Ui.TableCell.End[invoice.Amount.ToString("C", CultureInfo.GetCultureInfo("en-US"))]
                    ])
                ]
            ]
        ];

    private Ui.TableColumnDirection Direction =>
        _newestFirst ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc;

    // The pager moves now; the rows are replaced when the read is back.
    private async Task Show(int page)
    {
        _page = page;
        _invoices = await Invoices();
    }

    private async Task TurnRound()
    {
        _newestFirst = !_newestFirst;
        _page = 1;
        _invoices = await Invoices();
    }

    private async Task<Invoice[]> Invoices()
    {
        if (_slow)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), CancellationToken);
        }

        return Read(_page, _newestFirst);
    }

    private static Invoice[] Read(int page, bool newestFirst) =>
    [
        .. Enumerable.Range(((page - 1) * PageSize) + 1, PageSize)
            .Select(row => newestFirst ? Count + 1 - row : row)
            .Select(number => new Invoice(number, Customers[number % Customers.Length], 20m + (number * 7.5m)))
    ];

    private sealed record Invoice(int Number, string Customer, decimal Amount);
}
