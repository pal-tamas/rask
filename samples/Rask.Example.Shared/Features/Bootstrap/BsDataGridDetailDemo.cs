namespace Rask.Example.Shared.Features;

// Master-detail: ExpandedContent gives every row an expander toggle and, when open, a full-width detail row
// underneath it. RowKey is what makes expansion stick to the row rather than to its position, so an open row
// stays open across a sort — try sorting with a row expanded.
public sealed partial class BsDataGridDetailDemo : Component
{
    private sealed new record Line(string Sku, int Qty, decimal Each);

    private sealed record Order(string Number, string Customer, DateOnly Placed, IReadOnlyList<Line> Lines)
    {
        public decimal Total => Lines.Sum(l => l.Qty * l.Each);
    }

    private static readonly List<Order> Orders =
    [
        new("SO-1041", "Northwind Ltd", new DateOnly(2026, 6, 2),
            [new("CUP-01", 24, 4.50m), new("SAU-01", 24, 2.75m)]),
        new("SO-1042", "Contoso", new DateOnly(2026, 6, 9),
            [new("GRN-11", 2, 129.50m)]),
        new("SO-1043", "Fabrikam", new DateOnly(2026, 6, 14),
            [new("DSK-40", 1, 599.00m), new("ARM-02", 2, 119.00m), new("LMP-07", 3, 39.00m)]),
    ];

    protected override Component? Render() =>
        Div(Id: "grid-detail-demo")[
        BsDataGrid(
            Id: "bs-grid-detail",
            Data: Orders,
            RowKey: o => o.Number,
            ExpandedContent: Lines,
            Columns:
            [
                new BsColumn<Order> { Title = "Order", Value = o => o.Number, Sortable = true },
                new BsColumn<Order> { Title = "Customer", Value = o => o.Customer, Sortable = true },
                new BsColumn<Order>
                {
                    Title = "Placed", Sortable = true, SortKey = o => o.Placed,
                    Value = o => o.Placed.ToString("yyyy-MM-dd"),
                },
                new BsColumn<Order>
                {
                    Title = "Total", Class = Txt.End(), Sortable = true, SortKey = o => o.Total,
                    Value = o => o.Total.ToString("C"),
                },
            ])];

    // The detail row is just a component — anything you can render belongs here.
    private static Component Lines(Order order) =>
        BsCard(Class: Margin.Bottom(0))[
            BsCardBody()[
                BsCardTitle()[$"Lines for {order.Number}"],
                BsTable(Small: true, Class: Margin.Bottom(0))[
                    Thead()[Tr()[
                        Th(Scope: "col")["SKU"],
                        Th(Scope: "col", Class: Txt.End())["Qty"],
                        Th(Scope: "col", Class: Txt.End())["Each"]
                    ]],
                    Tbody()[order.Lines.Select(l => Tr(Key: l.Sku)[
                        Td()[l.Sku],
                        Td(Class: Txt.End())[l.Qty.ToString()],
                        Td(Class: Txt.End())[l.Each.ToString("C")]
                    ])]
                ]
            ]
        ];
}
