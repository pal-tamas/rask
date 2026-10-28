using System.Globalization;

namespace Rask.Site.Features;

// Master-detail datagrid, embedded in docs/composition.md's "Keyed lists" section (its standalone
// /master-detail page folded in). Expand/collapse and both grids' sort live in plain component fields.
//
// Expanding a row inserts a second, keyed <tr> ("detail-{id}") right after the keyed main row ("{id}").
// Because every row carries a stable Key, the live diff treats expand as an in-place keyed Insert and
// collapse as a keyed Remove — sibling expanded rows keep their own inner sort across the reconcile. Each
// detail panel hosts its own plain <table> of line items with an independent sort, so the demo owns three
// pieces of state: the expanded set, the outer sort, and a per-order inner sort.
public sealed partial class MasterDetailDemo : Component
{
    private static readonly Order[] _orders = BuildOrders();

    // (id, header, sortable, width) — the expander column has no label and no sort. Every column but one has
    // a width, so a sort moves the rows and never the columns; the one without takes the rest.
    private static readonly (string Id, string Header, bool Sortable, string? Width)[] _orderColumns =
    [
        ("expander", "", false, "w-14"),
        ("customer", "Customer", true, null),
        ("placed", "Placed", true, "w-32"),
        ("status", "Status", true, "w-36"),
        ("items", "Items", true, "w-24"),
        ("total", "Total", true, "w-32")
    ];

    private static readonly (string Id, string Header, string? Width)[] _itemColumns =
    [
        ("sku", "SKU", "w-32"),
        ("product", "Product", null),
        ("qty", "Qty", "w-20"),
        ("unit", "Unit price", "w-32"),
        ("line", "Line total", "w-32")
    ];

    // Expanded order ids, the outer sort, and the inner sort per order — all local UI state.
    // A sort is a (column id, ascending) pair; an empty column id means "unsorted".
    private readonly HashSet<int> _expanded = new();
    private readonly Dictionary<int, (string Col, bool Asc)> _itemSort = new();
    private (string Col, bool Asc) _orderSort = ("", true);

    protected override Component? Render()
    {
        var orders = SortOrders(_orders, _orderSort);

        return Ui.Card[Ui.CardBleed[
            Ui.Table.Id("md-orders").Class("w-full min-w-190 [&_tbody_tr:hover]:bg-zinc-50 dark:bg-zinc-900")[
                Ui.TableColumns.Class("bg-zinc-50 dark:bg-zinc-900")[
                    _orderColumns.Select(c =>
                        c.Sortable
                            ? SortHeader(c.Id, c.Header, c.Width, _orderSort, ToggleOrderSort)
                            : Ui.TableColumn.Key(c.Id).Class(c.Width))
                ],
                Ui.TableRows[BuildOrderRows(orders)]
            ]
        ]];
    }

    private List<Component> BuildOrderRows(IReadOnlyList<Order> orders)
    {
        var rows = new List<Component>(orders.Count * 2);
        foreach (var order in orders)
        {
            var open = _expanded.Contains(order.Id);

            rows.Add(Ui.TableRow.Key(order.Id).Class("md-row")[
                Ui.TableCell[
                    Ui.Button.Subtle.Icon(open ? Ui.IconName.ChevronDown : Ui.IconName.ChevronRight)
                        .AriaLabel(open ? $"Collapse order {order.Id}" : $"Expand order {order.Id}")
                        .Class("p-0 no-underline")
                        .Data("testid", $"expander-{order.Id}")
                        .OnClick(() => Toggle(order.Id))
                ],
                Ui.TableCell.Class("truncate font-semibold").Title(order.Customer)[order.Customer],
                Ui.TableCell.Class("text-zinc-500 dark:text-white/70! text-sm")[order.Placed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
                Ui.TableCell[Ui.Badge.Color(StatusColor(order.Status))[order.Status]],
                Ui.TableCell.Class("text-zinc-500 dark:text-white/70!")[order.Items.Count],
                Ui.TableCell.Style("text-align:right; font-variant-numeric:tabular-nums;")[
                    "$" + order.Total.ToString("N2", CultureInfo.InvariantCulture)
                ]
            ]);

            if (open)
            {
                rows.Add(Ui.TableRow.Key($"detail-{order.Id}").Class("md-detail")[
                    Ui.TableCell.Attributes(("colspan", _orderColumns.Length.ToString(CultureInfo.InvariantCulture))).Class("p-0 bg-zinc-50 dark:bg-zinc-900")[
                        Div
                            .Class("p-3")
                            .Data("testid", $"inner-{order.Id}")[
                            InnerGrid(order)
                        ]
                    ]
                ]);
            }
        }

        return rows;
    }

    private Component InnerGrid(Order order)
    {
        var sort = _itemSort.GetValueOrDefault(order.Id, ("", true));
        var items = SortItems(order.Items, sort);

        return Ui.Table.Class("w-full min-w-168 [&_tbody_tr:nth-child(odd)]:bg-zinc-50 dark:bg-zinc-900 bg-white dark:bg-zinc-800")[
            Ui.TableColumns[
                _itemColumns.Select(c => SortHeader(c.Id, c.Header, c.Width, sort, col => ToggleItemSort(order.Id, col)))
            ],
            Ui.TableRows[
                items.Select(it =>
                    Ui.TableRow.Key(it.Id)[
                        Ui.TableCell[Code[it.Sku]],
                        Ui.TableCell.Class("truncate").Title(it.Product)[it.Product],
                        Ui.TableCell.Class("text-zinc-500 dark:text-white/70!")[it.Qty],
                        Ui.TableCell.Style("text-align:right; font-variant-numeric:tabular-nums;")[
                            "$" + it.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)
                        ],
                        Ui.TableCell.Style("text-align:right; font-variant-numeric:tabular-nums;")[
                            "$" + it.LineTotal.ToString("N2", CultureInfo.InvariantCulture)
                        ]
                    ])
            ]
        ];
    }

    private void Toggle(int id)
    {
        if (!_expanded.Add(id))
        {
            _expanded.Remove(id);
        }

        StateHasChanged();
    }

    private void ToggleOrderSort(string col)
    {
        _orderSort = NextSort(_orderSort, col);
        StateHasChanged();
    }

    private void ToggleItemSort(int orderId, string col)
    {
        _itemSort[orderId] = NextSort(_itemSort.GetValueOrDefault(orderId, ("", true)), col);
        StateHasChanged();
    }

    // Cycle a column's sort: unsorted → asc → desc → unsorted.
    private static (string Col, bool Asc) NextSort((string Col, bool Asc) current, string col) =>
        current switch
        {
            _ when !string.Equals(current.Col, col, StringComparison.Ordinal) => (col, true),
            { Asc: true } => (col, false),
            _ => ("", true),
        };

    private static IReadOnlyList<Order> SortOrders(IReadOnlyList<Order> source, (string Col, bool Asc) sort)
    {
        if (sort.Col.Length == 0)
        {
            return source;
        }

        var asc = sort.Asc;
        IEnumerable<Order> view = source;
        view = sort.Col switch
        {
            "customer" => asc ? view.OrderBy(o => o.Customer, StringComparer.Ordinal) : view.OrderByDescending(o => o.Customer, StringComparer.Ordinal),
            "placed" => asc ? view.OrderBy(o => o.Placed) : view.OrderByDescending(o => o.Placed),
            "status" => asc ? view.OrderBy(o => o.Status, StringComparer.Ordinal) : view.OrderByDescending(o => o.Status, StringComparer.Ordinal),
            "items" => asc ? view.OrderBy(o => o.Items.Count) : view.OrderByDescending(o => o.Items.Count),
            "total" => asc ? view.OrderBy(o => o.Total) : view.OrderByDescending(o => o.Total),
            _ => view
        };
        return view.ToArray();
    }

    private static IReadOnlyList<LineItem> SortItems(IReadOnlyList<LineItem> source, (string Col, bool Asc) sort)
    {
        if (sort.Col.Length == 0)
        {
            return source;
        }

        var asc = sort.Asc;
        IEnumerable<LineItem> view = source;
        view = sort.Col switch
        {
            "sku" => asc ? view.OrderBy(i => i.Sku, StringComparer.Ordinal) : view.OrderByDescending(i => i.Sku, StringComparer.Ordinal),
            "product" => asc ? view.OrderBy(i => i.Product, StringComparer.Ordinal) : view.OrderByDescending(i => i.Product, StringComparer.Ordinal),
            "qty" => asc ? view.OrderBy(i => i.Qty) : view.OrderByDescending(i => i.Qty),
            "unit" => asc ? view.OrderBy(i => i.UnitPrice) : view.OrderByDescending(i => i.UnitPrice),
            "line" => asc ? view.OrderBy(i => i.LineTotal) : view.OrderByDescending(i => i.LineTotal),
            _ => view
        };
        return view.ToArray();
    }

    // Shared sort-aware header: a link button that toggles the column's sort, with a chevron reflecting
    // its current direction. Used by both the outer and the inner grid.
    // The table draws the sort button and its chevron; the page keeps which column is sorted and which way.
    private static Component SortHeader(string columnId, string header, string? width, (string Col, bool Asc) sort,
        Action<string> toggle) =>
        Ui.TableColumn
            .Key(columnId)
            .Class(width)
            .Sortable()
            .Sorted(string.Equals(sort.Col, columnId, StringComparison.Ordinal))
            .Direction(sort.Asc ? Ui.TableColumnDirection.Asc : Ui.TableColumnDirection.Desc)
            .OnSort(() => toggle(columnId))[header];

    // A COLOUR, not a class: the call site names the hue and Ui.Badge decides the tint, the text and dark mode.
    private static Ui.Color? StatusColor(string status) => status switch
    {
        "Shipped" => Ui.Color.Green,
        "Processing" => Ui.Color.Blue,
        "Pending" => Ui.Color.Yellow,
        "Cancelled" => Ui.Color.Red,
        _ => null
    };

    private static Order[] BuildOrders()
    {
        var customers = new[]
        {
            "Ada Lovelace", "Grace Hopper", "Linus Torvalds", "Margaret Hamilton", "Donald Knuth",
            "Barbara Liskov", "Edsger Dijkstra", "Tony Hoare", "Alan Turing", "John Backus",
            "Niklaus Wirth", "Bjarne Stroustrup", "Anders Hejlsberg", "James Gosling"
        };
        var statuses = new[] { "Shipped", "Processing", "Pending", "Cancelled" };
        var products = new[]
        {
            ("KBD-01", "Mechanical keyboard", 89m), ("MSE-02", "Wireless mouse", 39m),
            ("MON-27", "27\" monitor", 329m), ("USB-C", "USB-C hub", 59m),
            ("CBL-HD", "HDMI cable", 12m), ("STD-01", "Laptop stand", 45m),
            ("WBC-4K", "4K webcam", 129m), ("HPN-01", "Noise-cancelling headphones", 199m),
            ("DSK-MAT", "Desk mat", 24m), ("CHR-ERG", "Ergonomic chair", 449m)
        };

#pragma warning disable S2245 // seeded on purpose: the same demo data on every visit, nothing secret
        var rng = new Random(42);
#pragma warning restore S2245
        var orders = new Order[customers.Length];
        var nextItemId = 1;
        for (var i = 0; i < customers.Length; i++)
        {
            var itemCount = 2 + rng.Next(0, 5);
            var items = new LineItem[itemCount];
            for (var j = 0; j < itemCount; j++)
            {
                var (sku, name, price) = products[rng.Next(products.Length)];
                items[j] = new LineItem(nextItemId++, i + 1, sku, name, 1 + rng.Next(0, 5), price);
            }

            var placed = new DateOnly(2025, 1 + rng.Next(0, 12), 1 + rng.Next(0, 28));
            orders[i] = new Order(i + 1, customers[i], placed, statuses[rng.Next(statuses.Length)], items);
        }

        return orders;
    }

    private sealed record LineItem(int Id, int OrderId, string Sku, string Product, int Qty, decimal UnitPrice)
    {
        public decimal LineTotal => Qty * UnitPrice;
    }

    private sealed record Order(
        int Id,
        string Customer,
        DateOnly Placed,
        string Status,
        IReadOnlyList<LineItem> Items)
    {
        public decimal Total => Items.Sum(i => i.LineTotal);
    }
}
