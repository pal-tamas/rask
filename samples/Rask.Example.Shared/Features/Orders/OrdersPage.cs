using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Master-detail datagrid. The sibling Data table page drives all of its state through the URL query
// string; this page is the deliberate contrast — expand/collapse and both grids' sort live in plain
// component fields. A click mutates a field and StateHasChanged() re-renders the page.
//
// Expanding a row inserts a second, keyed <tr> ("detail-{id}") right after the keyed main row
// ("{id}"). Because every row carries a stable Key, the live diff treats expand as an in-place keyed
// Insert and collapse as a keyed Remove — sibling expanded rows keep their own inner sort across the
// reconcile. Each detail panel hosts its own plain <table> of line items with an independent sort, so
// the page owns three pieces of state: the expanded set, the outer sort, and a per-order inner sort.
[Route("master-detail")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class OrdersPage : Component
{
    private static readonly Order[] _orders = BuildOrders();

    // (id, header, sortable) — the expander column has no label and no sort.
    private static readonly (string Id, string Header, bool Sortable)[] _orderColumns =
    [
        ("expander", "", false),
        ("customer", "Customer", true),
        ("placed", "Placed", true),
        ("status", "Status", true),
        ("items", "Items", true),
        ("total", "Total", true)
    ];

    private static readonly (string Id, string Header)[] _itemColumns =
    [
        ("sku", "SKU"),
        ("product", "Product"),
        ("qty", "Qty"),
        ("unit", "Unit price"),
        ("line", "Line total")
    ];

    // Expanded order ids, the outer sort, and the inner sort per order — all local UI state.
    // A sort is a (column id, ascending) pair; an empty column id means "unsorted".
    private readonly HashSet<int> _expanded = new();
    private readonly Dictionary<int, (string Col, bool Asc)> _itemSort = new();
    private (string Col, bool Asc) _orderSort = ("", true);

    protected override RenderResult Head => Title()["Master-detail — Rask"];

    protected override RenderResult Render()
    {
        var orders = SortOrders(_orders, _orderSort);

        return
        [
            PageHeader.Render(
                "Master-detail",
                "Collapsible rows with a nested datagrid. Click a row to reveal its line items in an inner, " +
                "independently sortable table. Expand/collapse and both grids' sort are held in plain component " +
                "fields — the deliberate contrast with the URL-driven Data table."),
            BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
                Div(Class: "table-responsive")[
                    Table(Id: "md-orders", Class: "table table-hover align-middle mb-0")[
                        Thead(Class: "table-light")[
                            Tr()[_orderColumns.Select(c =>
                                c.Sortable
                                    ? SortHeader(c.Id, c.Header, _orderSort, ToggleOrderSort)
                                    : Th(Scope: "col", Key: c.Id))]
                        ],
                        Tbody()[BuildOrderRows(orders)]
                    ]
                ]
            ],
            P(Class: "small text-secondary mt-3 mb-0")[
                "Expand state is a ",
                Code()["HashSet<int>"],
                " on the page; toggling it calls ",
                Code()["StateHasChanged()"],
                ". Each open row inserts a keyed ",
                Code()["<tr>"],
                " detail row, so the live diff reconciles it as an in-place insert/remove and the other open rows " +
                "keep their own inner sort. The detail panel hosts a second plain ",
                Code()["Table"],
                " of line items with its own sort."
            ],
            CodeSample(
                ["OrdersPage.cs"],
                Title: "Source",
                Notes:
                "The whole page above, verbatim. Expand state is a HashSet<int>, the outer sort and each order's " +
                "inner sort are plain fields — a click mutates one and StateHasChanged() re-renders. Open rows " +
                "insert a keyed detail <tr>, so the live diff reconciles them as in-place insert/remove and sibling " +
                "open rows keep their own inner sort.")
        ];
    }

    private List<Child> BuildOrderRows(IReadOnlyList<Order> orders)
    {
        var rows = new List<Child>(orders.Count * 2);
        foreach (var order in orders)
        {
            var open = _expanded.Contains(order.Id);

            rows.Add(Tr(Key: order.Id, Class: "md-row")[
                Td(Style: "width:44px;")[
                    Button(Class: "btn btn-sm btn-link p-0 text-decoration-none", Data: new Dictionary<string, string?> { ["testid"] = $"expander-{order.Id}" }, OnClick: () => Toggle(order.Id))[
                        I(Class: open ? "bi bi-chevron-down" : "bi bi-chevron-right")
                    ]
                ],
                Td(Class: "fw-semibold")[order.Customer],
                Td(Class: "text-secondary small")[order.Placed.ToString("yyyy-MM-dd")],
                Td()[Span(Class: $"badge {StatusBadge(order.Status)}")[order.Status]],
                Td(Class: "text-secondary")[order.Items.Count],
                Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                    "$" + order.Total.ToString("N2", CultureInfo.InvariantCulture)
                ]
            ]);

            if (open)
            {
                rows.Add(Tr(Key: $"detail-{order.Id}", Class: "md-detail")[
                    Td(Colspan: _orderColumns.Length, Class: "p-0 bg-light")[
                        Div(
                            Class: "p-3",
                            Data: new Dictionary<string, string?> { ["testid"] = $"inner-{order.Id}" })[
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

        return Table(Class: "table table-sm table-striped align-middle mb-0 bg-white")[
            Thead()[
                Tr()[_itemColumns.Select(c =>
                    SortHeader(c.Id, c.Header, sort, col => ToggleItemSort(order.Id, col)))]
            ],
            Tbody()[
                items.Select(it =>
                    Tr(Key: it.Id)[
                        Td()[Code()[it.Sku]],
                        Td()[it.Product],
                        Td(Class: "text-secondary")[it.Qty],
                        Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                            "$" + it.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)
                        ],
                        Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
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
        current.Col != col ? (col, true)
        : current.Asc ? (col, false)
        : ("", true);

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
            "customer" => asc ? view.OrderBy(o => o.Customer) : view.OrderByDescending(o => o.Customer),
            "placed" => asc ? view.OrderBy(o => o.Placed) : view.OrderByDescending(o => o.Placed),
            "status" => asc ? view.OrderBy(o => o.Status) : view.OrderByDescending(o => o.Status),
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
            "sku" => asc ? view.OrderBy(i => i.Sku) : view.OrderByDescending(i => i.Sku),
            "product" => asc ? view.OrderBy(i => i.Product) : view.OrderByDescending(i => i.Product),
            "qty" => asc ? view.OrderBy(i => i.Qty) : view.OrderByDescending(i => i.Qty),
            "unit" => asc ? view.OrderBy(i => i.UnitPrice) : view.OrderByDescending(i => i.UnitPrice),
            "line" => asc ? view.OrderBy(i => i.LineTotal) : view.OrderByDescending(i => i.LineTotal),
            _ => view
        };
        return view.ToArray();
    }

    // Shared sort-aware header: a link button that toggles the column's sort, with a chevron reflecting
    // its current direction. Used by both the outer and the inner grid.
    private static Component SortHeader(string columnId, string header, (string Col, bool Asc) sort,
        Action<string> toggle)
    {
        var sorted = sort.Col == columnId;
        var icon = sorted
            ? sort.Asc ? "bi-chevron-up" : "bi-chevron-down"
            : "bi-chevron-expand text-secondary opacity-50";

        return Th(Scope: "col", Key: columnId)[
            Button(
                "button",
                Class: "btn btn-link p-0 text-decoration-none text-dark fw-semibold d-inline-flex " +
                       "align-items-center gap-1",
                OnClick: () => toggle(columnId))[
                Span()[header],
                I(Class: $"bi {icon} small")
            ]
        ];
    }

    private static string StatusBadge(string status) => status switch
    {
        "Shipped" => "text-bg-success",
        "Processing" => "text-bg-primary",
        "Pending" => "text-bg-warning",
        "Cancelled" => "text-bg-danger",
        _ => "text-bg-secondary"
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

        var rng = new Random(42);
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
