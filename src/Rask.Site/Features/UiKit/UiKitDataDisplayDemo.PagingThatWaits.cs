using System.Globalization;

namespace Rask.Site.Features.UiKit;

// Paging that waits: the handler moves the page and awaits the read, and nothing the reader sees changes until
// the read is back. A slow one dims the rows it will replace — the table does that by itself.
public sealed partial class UiKitDataDisplayDemo
{
    private const int InvoicePageSize = 5;
    private const int InvoiceCount = 40;

    private static readonly TimeSpan SlowRead = TimeSpan.FromMilliseconds(1500);

    private int _invoicePage = 1;
    private bool _invoicesNewestFirst;
    private bool _invoiceReadIsSlow;
    private Invoice[] _invoices = ReadInvoices(1, newestFirst: false);

    private Component PagingThatWaitsSection() =>
        Section(
            "Paging that waits",
            "Nothing changes until the next page has its rows. A read that is back within 150 ms is one paint: "
            + "pager and rows together. Make the read slow and the pager moves first, the rows you have stay and "
            + "dim, and the new ones take their place when they arrive.",
            Div.Data(Testid("ui-table-waits")).Class("grid gap-3")[
                Div[
                    Ui.Button.Sm.Data(Testid("ui-invoices-slow")).Loading(false).OnClick(() => { _invoiceReadIsSlow = !_invoiceReadIsSlow; })[
                        _invoiceReadIsSlow ? "The read takes 1.5 s" : "The read is instant"
                    ]
                ],
                Ui.Table
                    .Id("ui-invoices")
                    .Class("w-full")
                    .ContainerClass("min-w-0")
                    .Paginate(Ui.Pagination
                        .Paginator(new UiPaginator { Page = _invoicePage, PerPage = InvoicePageSize, Total = InvoiceCount })
                        .OnPage(ShowInvoices))[
                    Ui.TableColumns[
                        Ui.TableColumn.Sortable().Sorted(true).Direction(InvoiceDirection).OnSort(TurnInvoicesRound)["Invoice"],
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
            ]);

    private Ui.TableColumnDirection InvoiceDirection =>
        _invoicesNewestFirst ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc;

    // The pager moves now; the rows are replaced when the read is back.
    private async Task ShowInvoices(int page)
    {
        _invoicePage = page;
        _invoices = await Invoices();
    }

    private async Task TurnInvoicesRound()
    {
        _invoicesNewestFirst = !_invoicesNewestFirst;
        _invoicePage = 1;
        _invoices = await Invoices();
    }

    private async Task<Invoice[]> Invoices()
    {
        if (_invoiceReadIsSlow)
        {
            await Task.Delay(SlowRead, CancellationToken);
        }

        return ReadInvoices(_invoicePage, _invoicesNewestFirst);
    }

    private static Invoice[] ReadInvoices(int page, bool newestFirst) =>
    [
        .. Enumerable.Range(((page - 1) * InvoicePageSize) + 1, InvoicePageSize)
            .Select(row => newestFirst ? InvoiceCount + 1 - row : row)
            .Select(number => new Invoice(number, OrderBook[number % OrderBook.Length].Customer, 20m + (number * 7.5m)))
    ];

    private sealed record Invoice(int Number, string Customer, decimal Amount);
}
