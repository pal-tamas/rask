using System.Globalization;

namespace Rask.Site.Features.UiKit;

// The table: Flux's examples, in its order — sortable and paged, bleeding out of a padded box, and held
// in place while it scrolls both ways.
public sealed partial class UiKitDataDisplayDemo
{
    private const int PageSize = 4;

    // Sticky parts need a surface of their own, or the rows show through them.
    private const string TableSurface = "bg-white dark:bg-zinc-900";

    private static readonly Order[] OrderBook =
    [
        new(428, "Lindsey Aminoff", new(2026, 7, 29), "Paid", 49m),
        new(427, "Hanna Lubin", new(2026, 7, 28), "Paid", 312m),
        new(426, "Kianna Bushevi", new(2026, 7, 30), "Refunded", 132m),
        new(424, "Gustavo Geidt", new(2026, 7, 27), "Paid", 31m),
        new(423, "Nolan George", new(2026, 7, 26), "Paid", 313m),
        new(421, "Desirae George", new(2026, 7, 31), "Paid", 32m),
        new(420, "Jackson Bothman", new(2026, 7, 30), "Refunded", 94m),
        new(419, "Hanna Lipshutz", new(2026, 7, 29), "Paid", 22m),
        new(418, "Alfredo Levin", new(2026, 7, 25), "Failed", 12m),
        new(417, "Zain Lubin", new(2026, 7, 28), "Paid", 91m),
    ];

    private string _sortBy = "date";
    private Ui.TableColumnDirection _direction = Ui.TableColumnDirection.Desc;
    private int _page = 1;

    private Component TableSection() =>
        Section(
            "Table",
            "The table sorts nothing and pages nothing: this page keeps the sorted column, its direction and the "
            + "page number, and the table draws them. A cell holding a badge drops its own vertical padding.",
            Div.Data(Testid("ui-table")).Class("grid gap-8")[
                SortedAndPaged(),
                BleedingTable(),
                Sticky()
            ]);

    private Component SortedAndPaged() =>
        Ui.Table
            .Key("sorted")
            .Id("ui-orders")
            .Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = _page, PerPage = PageSize, Total = OrderBook.Length }).OnPage(page => { _page = page; }))[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                SortableColumn("date", "Date"),
                SortableColumn("status", "Status"),
                SortableColumn("amount", "Amount", Ui.Align.End)
            ],
            Ui.TableRows[
                Sorted().Skip((_page - 1) * PageSize).Take(PageSize).Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell[order.Customer],
                    Ui.TableCell[Date(order)],
                    Ui.TableCell.Class("py-0")[StatusBadge(order)],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[Money(order)]
                ])
            ]
        ];

    private Component SortableColumn(string column, string label, Ui.Align? align = null) =>
        Ui.TableColumn
            .Sortable()
            .Sorted(IsSortedBy(column))
            .Direction(_direction)
            .Align(align)
            .OnSort(() => SortBy(column))[label];

    private bool IsSortedBy(string column) => string.Equals(_sortBy, column, StringComparison.Ordinal);

    // A second click on the sorted column turns it round; a first click on another starts it ascending.
    private void SortBy(string column)
    {
        _direction = IsSortedBy(column) && _direction == Ui.TableColumnDirection.Asc
            ? Ui.TableColumnDirection.Desc
            : Ui.TableColumnDirection.Asc;
        _sortBy = column;
        _page = 1;
    }

    private IEnumerable<Order> Sorted()
    {
        Func<Order, IComparable> key = _sortBy switch
        {
            "status" => order => order.Status,
            "amount" => order => order.Amount,
            _ => order => order.Placed,
        };

        return _direction == Ui.TableColumnDirection.Asc ? OrderBook.OrderBy(key) : OrderBook.OrderByDescending(key);
    }

    // The box states how far the table may run through its padding; a card states its own.
    private static Component BleedingTable() =>
        Div.Key("bleed").Class("max-w-md rounded-lg border border-zinc-200 p-4 [--ui-bleed:1rem] dark:border-zinc-700")[
            P.Class("text-sm font-medium")["Recent customers"],
            Ui.Table.Bleed().ContainerClass("mt-4")[
                Ui.TableColumns[
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn["Status"],
                    Ui.TableColumn.End["Amount"]
                ],
                Ui.TableRows[
                    OrderBook.Take(3).Select(order => Ui.TableRow.Key(order.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Customer],
                        Ui.TableCell.Class("py-0")[StatusBadge(order)],
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[Money(order)]
                    ])
                ]
            ]
        ];

    private static Component Sticky() =>
        Ui.Table.Key("sticky").ContainerClass("max-h-64 max-w-md")[
            Ui.TableColumns.Sticky().Class(TableSurface)[
                Ui.TableColumn.Sticky().Class(TableSurface)["ID"],
                Ui.TableColumn["Customer"],
                Ui.TableColumn["Email"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn.End["Amount"]
            ],
            Ui.TableRows[
                OrderBook.Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell.Sticky().Class(TableSurface)[order.Id.ToString(CultureInfo.InvariantCulture)],
                    Ui.TableCell[order.Customer],
                    Ui.TableCell[order.Customer.Split(' ')[0].ToLowerInvariant() + "@example.com"],
                    Ui.TableCell[Date(order)],
                    Ui.TableCell.Class("py-0")[StatusBadge(order)],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[Money(order)]
                ])
            ]
        ];

    private static Component StatusBadge(Order order) =>
        Ui.Badge.Sm.Color(order.Status switch
        {
            "Paid" => Ui.Color.Green,
            "Failed" => Ui.Color.Red,
            _ => null,
        })[order.Status];

    private static string Date(Order order) => order.Placed.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string Money(Order order) => order.Amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private sealed record Order(int Id, string Customer, DateOnly Placed, string Status, decimal Amount);
}
