using System.Globalization;
using Rask.Core.Routing;
using Rask.Core.Tables;

namespace Rask.Example.Shared.Features;

// Master-detail datagrid. The sibling Data table page drives all of its state through the URL query
// string; this page is the deliberate contrast — expand/collapse and both grids' sort live in plain
// component fields. A click mutates a field and the auto-wrapped callback re-renders the page (exactly
// how ShowcaseLayout's drawer toggle works), so no BypassRenderCache is needed.
//
// Expanding a row inserts a second, keyed <tr> ("detail-{id}") right after the keyed main row
// ("{id}"). Because every row carries a stable Key, the live diff treats expand as an in-place keyed
// Insert and collapse as a keyed Remove — sibling expanded rows keep their own inner sort across the
// reconcile. Each detail panel hosts its own controlled TableModel<LineItem>, so the page owns three
// independent pieces of state: the expanded set, the outer sort, and a per-order inner sort.
[Route("master-detail")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class OrdersPage : Component
{
    private static readonly Order[] _orders = BuildOrders();

    private static readonly IReadOnlyList<ColumnDef<Order>> _orderColumns =
    [
        new() { Id = "expander", Header = "", Sortable = false },
        new() { Id = "customer", Header = "Customer" },
        new() { Id = "placed", Header = "Placed" },
        new() { Id = "status", Header = "Status" },
        new() { Id = "items", Header = "Items" },
        new() { Id = "total", Header = "Total" }
    ];

    private static readonly IReadOnlyList<ColumnDef<LineItem>> _itemColumns =
    [
        new() { Id = "sku", Header = "SKU" },
        new() { Id = "product", Header = "Product" },
        new() { Id = "qty", Header = "Qty" },
        new() { Id = "unit", Header = "Unit price" },
        new() { Id = "line", Header = "Line total" }
    ];

    // Expanded order ids, the outer sort, and the inner sort per order — all local UI state.
    private readonly HashSet<int> _expanded = new();
    private readonly Dictionary<int, IReadOnlyList<ColumnSort>> _itemSort = new();
    private IReadOnlyList<ColumnSort> _orderSort = [];

    protected override RenderResult Head => Title()["Master-detail — Rask"];

    protected override RenderResult Render()
    {
        var orders = SortOrders(_orders, _orderSort);

        return
        [
            PageHeader.Render(
                "Master-detail",
                "Collapsible rows with a nested datagrid. Click a row to reveal its line items in an " +
                "inner, independently sortable TableModel<T>. Expand/collapse and both grids' sort are " +
                "held in plain component fields — the deliberate contrast with the URL-driven Data table."),
            TableModel<Order>(
                ctx => Div(Class: "card shadow-sm border-0")[
                    Div(Class: "table-responsive")[
                        Table(Id: "md-orders", Class: "table table-hover align-middle mb-0")[
                            Thead(Class: "table-light")[
                                Tr()[ctx.Headers.Select(h => OrderHeader(h))]
                            ],
                            Tbody()[BuildOrderRows(ctx)]
                        ]
                    ]
                ],
                Columns: _orderColumns,
                Rows: orders,
                KeySelector: o => o.Id,
                Sort: _orderSort,
                OnSort: sort =>
                {
                    _orderSort = sort;
                    StateHasChanged();
                }),
            P(Class: "small text-secondary mt-3 mb-0")[
                "Expand state is a ",
                Code()["HashSet<int>"],
                " on the page; toggling it re-renders through the callback auto-wrap. Each open row " +
                "inserts a keyed ",
                Code()["<tr>"],
                " detail row, so the live diff reconciles it as an in-place insert/remove and the other " +
                "open rows keep their own inner sort. The detail panel hosts a second controlled ",
                Code()["TableModel<LineItem>"],
                " — headless grids all the way down."
            ]
        ];
    }

    private List<Child> BuildOrderRows(TableModelContext<Order> ctx)
    {
        var rows = new List<Child>(ctx.Rows.Count * 2);
        foreach (var row in ctx.Rows)
        {
            var order = row.Value;
            var open = _expanded.Contains(order.Id);

            rows.Add(Tr(Key: row.Key, Class: "md-row")[
                Td(Style: "width:44px;")[
                    Button(
                        "button",
                        Class: "btn btn-sm btn-link p-0 text-decoration-none",
                        Data: new Dictionary<string, string?> { ["testid"] = $"expander-{order.Id}" },
                        OnClick: () => Toggle(order.Id))[
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
                    Td(Colspan: _orderColumns.Count, Class: "p-0 bg-light")[
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
        var sort = _itemSort.GetValueOrDefault(order.Id, []);
        var items = SortItems(order.Items, sort);

        return TableModel<LineItem>(
            ctx => Table(Class: "table table-sm table-striped align-middle mb-0 bg-white")[
                Thead()[Tr()[ctx.Headers.Select(h => ItemHeader(h))]],
                Tbody()[
                    ctx.Rows.Select(row =>
                        Tr(Key: row.Key)[
                            Td()[Code()[row.Value.Sku]],
                            Td()[row.Value.Product],
                            Td(Class: "text-secondary")[row.Value.Qty],
                            Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                                "$" + row.Value.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)
                            ],
                            Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                                "$" + row.Value.LineTotal.ToString("N2", CultureInfo.InvariantCulture)
                            ]
                        ])
                ]
            ],
            Columns: _itemColumns,
            Rows: items,
            KeySelector: it => it.Id,
            Sort: sort,
            OnSort: next =>
            {
                _itemSort[order.Id] = next;
                StateHasChanged();
            },
            Key: $"inner-model-{order.Id}");
    }

    private void Toggle(int id)
    {
        if (!_expanded.Add(id))
        {
            _expanded.Remove(id);
        }

        StateHasChanged();
    }

    private static IReadOnlyList<Order> SortOrders(IReadOnlyList<Order> source, IReadOnlyList<ColumnSort> sort)
    {
        if (sort.Count == 0)
        {
            return source;
        }

        var (col, asc) = (sort[0].ColumnId, sort[0].Direction == SortDirection.Ascending);
        IEnumerable<Order> view = source;
        view = col switch
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

    private static IReadOnlyList<LineItem> SortItems(IReadOnlyList<LineItem> source, IReadOnlyList<ColumnSort> sort)
    {
        if (sort.Count == 0)
        {
            return source;
        }

        var (col, asc) = (sort[0].ColumnId, sort[0].Direction == SortDirection.Ascending);
        IEnumerable<LineItem> view = source;
        view = col switch
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

    private static Component OrderHeader(HeaderCell header) =>
        header.Sortable ? SortHeader(header) : Th(Scope: "col", Key: header.ColumnId);

    private static Component ItemHeader(HeaderCell header) => SortHeader(header);

    // Shared sort-aware header: a link button that proposes the next sort, with a chevron reflecting
    // the column's current direction. Used by both the outer and the inner grid.
    private static Component SortHeader(HeaderCell header)
    {
        var icon = header.Direction switch
        {
            SortDirection.Ascending => "bi-chevron-up",
            SortDirection.Descending => "bi-chevron-down",
            _ => "bi-chevron-expand text-secondary opacity-50"
        };

        return Th(Scope: "col", Key: header.ColumnId)[
            Button(
                "button",
                Class: "btn btn-link p-0 text-decoration-none text-dark fw-semibold d-inline-flex " +
                       "align-items-center gap-1",
                OnClick: header.ToggleSort)[
                Span()[header.Header],
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
