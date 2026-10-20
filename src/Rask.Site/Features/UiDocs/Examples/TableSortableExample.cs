using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableSortableExample : Component
{
    private const int PageSize = 4;

    private static readonly Order[] Orders =
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

    protected override Component? Render() =>
        Ui.Table
            .Id("ui-orders")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _page, PerPage = PageSize, Total = Orders.Length })
                .OnPage(page => { _page = page; }))[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                Column("date", "Date"),
                Column("status", "Status"),
                Column("amount", "Amount", Ui.Align.End)
            ],
            Ui.TableRows[
                Sorted().Skip((_page - 1) * PageSize).Take(PageSize).Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell[order.Customer],
                    Ui.TableCell[order.Placed.ToString("MMM d", CultureInfo.InvariantCulture)],
                    Ui.TableCell.Class("py-0")[Ui.Badge.Sm.Color(Hue(order.Status))[order.Status]],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).End[order.Amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"))]
                ])
            ]
        ];

    private Component Column(string column, string label, Ui.Align? align = null) =>
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

        return _direction == Ui.TableColumnDirection.Asc ? Orders.OrderBy(key) : Orders.OrderByDescending(key);
    }

    private static Ui.Color? Hue(string status) => status switch
    {
        "Paid" => Ui.Color.Green,
        "Failed" => Ui.Color.Red,
        _ => null,
    };

    private sealed record Order(int Id, string Customer, DateOnly Placed, string Status, decimal Amount);
}
