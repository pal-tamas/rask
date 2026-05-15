using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("data-grid")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DataGridPage : Component
{
    private sealed record Order(int Id, string Customer, string Country, decimal Total, DateOnly Placed);

    private static readonly Order[] _orders =
    [
        new(1001, "Ada Lovelace",   "UK", 412.50m, new(2026, 3, 5)),
        new(1002, "Grace Hopper",   "US", 980.00m, new(2026, 3, 7)),
        new(1003, "Linus Torvalds", "FI", 125.10m, new(2026, 3, 9)),
        new(1004, "Margaret Hamilton","US", 540.25m, new(2026, 3, 11)),
        new(1005, "Donald Knuth",   "US", 815.00m, new(2026, 3, 12)),
        new(1006, "Barbara Liskov", "US", 220.00m, new(2026, 3, 15)),
        new(1007, "Edsger Dijkstra","NL", 1490.40m, new(2026, 3, 16)),
        new(1008, "Tony Hoare",     "UK", 360.00m, new(2026, 3, 17)),
        new(1009, "Alan Turing",    "UK", 1200.00m, new(2026, 3, 20)),
        new(1010, "John Backus",    "US", 95.75m, new(2026, 3, 21)),
        new(1011, "Niklaus Wirth",  "CH", 280.30m, new(2026, 3, 22)),
        new(1012, "Bjarne Stroustrup","DK", 705.00m, new(2026, 3, 23)),
        new(1013, "Rasmus Lerdorf", "DK", 60.00m, new(2026, 3, 25)),
        new(1014, "Brendan Eich",   "US", 1340.00m, new(2026, 3, 26)),
        new(1015, "Anders Hejlsberg","DK", 980.00m, new(2026, 3, 28)),
        new(1016, "Guido van Rossum","NL", 410.00m, new(2026, 3, 29)),
        new(1017, "James Gosling",  "CA", 770.00m, new(2026, 3, 30)),
        new(1018, "Yukihiro Matsumoto","JP", 200.00m, new(2026, 4, 1)),
        new(1019, "Larry Wall",     "US", 50.00m, new(2026, 4, 2)),
        new(1020, "Joe Armstrong",  "SE", 1830.00m, new(2026, 4, 4)),
    ];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Data grid",
                "Headless: DataGrid emits no DOM of its own. Compose your own Table/Tr/Td and let DataGridRows, DataGridSortButton, and DataGridPager wire in the state."),
            P(Class: "small text-secondary mb-4")[
                "Click a column header to sort. Shift-click to add a secondary sort. The pager controls the current page; click ",
                Strong()["Next"], " / ", Strong()["Prev"], " to navigate."
            ],
            CodeSample(
                """
                DataGrid(Source: _orders, PageSize: 6)[
                  Table()[
                    Thead()[Tr()[
                      Th()[DataGridSortButton<Order>(r => r.Id)["#"]],
                      Th()[DataGridSortButton<Order>(r => r.Customer)["Customer"]],
                      Th()[DataGridSortButton<Order>(r => r.Country)["Country"]],
                      Th()[DataGridSortButton<Order>(r => r.Total)["Total"]],
                      Th()[DataGridSortButton<Order>(r => r.Placed)["Placed"]]
                    ]],
                    Tbody()[DataGridRows<Order>(o => Tr()[
                      Td()[o.Id.ToString()],
                      Td()[o.Customer],
                      Td()[o.Country],
                      Td()[$"{o.Total:C}"],
                      Td()[o.Placed.ToString("yyyy-MM-dd")]
                    ])]
                  ],
                  DataGridPager()
                ]
                """,
                Notes: "Tip: Shift-click 'Country' after clicking 'Total' to sort by Total within each country.",
                Result: BuildGrid())
        ];

    private Component BuildGrid() =>
        Div(Class: "data-grid-demo")[
            DataGrid<Order>(Source: _orders, PageSize: 6)[
                Table(Class: "table table-sm align-middle mb-3")[
                    Thead(Class: "table-light")[
                        Tr()[
                            Th(Class: "p-0")[SortHeader<Order>(o => o.Id, "#")],
                            Th(Class: "p-0")[SortHeader<Order>(o => o.Customer, "Customer")],
                            Th(Class: "p-0")[SortHeader<Order>(o => o.Country, "Country")],
                            Th(Class: "p-0 text-end")[SortHeader<Order>(o => o.Total, "Total")],
                            Th(Class: "p-0")[SortHeader<Order>(o => o.Placed, "Placed")]
                        ]
                    ],
                    Tbody()[
                        DataGridRows<Order>(Row: o => Tr()[
                            Td()[o.Id.ToString()],
                            Td()[o.Customer],
                            Td()[o.Country],
                            Td(Class: "text-end")[$"{o.Total:C}"],
                            Td()[o.Placed.ToString("yyyy-MM-dd")]
                        ])
                    ]
                ],
                Nav(Class: "d-flex justify-content-center")[
                    DataGridPager(Template: state => Div(Class: "btn-group")[
                        Button(
                            Type: "button",
                            Class: "btn btn-outline-secondary btn-sm",
                            Disabled: state.CurrentPage <= 0,
                            OnClick: () => state.Prev())[I(Class: "bi bi-chevron-left me-1"), "Prev"],
                        Span(Class: "btn btn-sm bg-light text-muted disabled")[
                            $"Page {state.CurrentPage + 1} of {state.PageCount}"
                        ],
                        Button(
                            Type: "button",
                            Class: "btn btn-outline-secondary btn-sm",
                            Disabled: state.CurrentPage >= state.PageCount - 1,
                            OnClick: () => state.Next())["Next", I(Class: "bi bi-chevron-right ms-1")]
                    ])
                ]
            ]
        ];

    private static Component SortHeader<TRow>(
        System.Linq.Expressions.Expression<Func<TRow, object?>> by,
        string label) =>
        Div(Class: "d-grid")[
            DataGridSortButton<TRow>(SortBy: by)[
                Span(Class: "btn btn-link w-100 text-start fw-semibold text-decoration-none p-2")[
                    label,
                    I(Class: "bi bi-arrow-down-up ms-1 small opacity-50")
                ]
            ]
        ];
}
