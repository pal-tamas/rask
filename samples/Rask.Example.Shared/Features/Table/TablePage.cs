using System.Globalization;
using Rask.Core.Routing;
using Rask.Core.Tables;

namespace Rask.Example.Shared.Features;

[Route("table")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TablePage(Navigator nav) : Component
{
    private static readonly Person[] _people = BuildPeople(120);

    // Column metadata for the headless TableModel<T>. The model uses Id/Header/Sortable to project
    // sort-aware header cells; this page renders each cell itself (badges, currency, dates), so no
    // Value accessor is needed.
    private static readonly IReadOnlyList<ColumnDef<Person>> _columns =
    [
        new() { Id = "id", Header = "#" },
        new() { Id = "name", Header = "Name" },
        new() { Id = "city", Header = "City" },
        new() { Id = "department", Header = "Department" },
        new() { Id = "salary", Header = "Salary" },
        new() { Id = "joined", Header = "Joined" }
    ];

    [QueryParam] public string? Filter { get; set; }
    [QueryParam("sort")] public string? SortKey { get; set; }
    [QueryParam("dir")] public string? Dir { get; set; }
    [QueryParam] public int? Page { get; set; }
    [QueryParam] public int? Size { get; set; }

    protected override RenderResult Head => Title()["Data table — Rask"];

    private static Person[] BuildPeople(int count)
    {
        var firsts = new[]
        {
            "Ada", "Grace", "Linus", "Margaret", "Donald", "Barbara", "Edsger", "Tony", "Alan", "John", "Niklaus",
            "Bjarne", "Anders", "James", "Brendan", "Guido"
        };
        var lasts = new[]
        {
            "Lovelace", "Hopper", "Torvalds", "Hamilton", "Knuth", "Liskov", "Dijkstra", "Hoare", "Turing",
            "Backus", "Wirth", "Stroustrup", "Hejlsberg", "Gosling", "Eich", "van Rossum"
        };
        var cities = new[]
        {
            "London", "New York", "Helsinki", "Boston", "Stanford", "Cambridge", "Amsterdam", "Oxford", "Berlin",
            "Tokyo", "Sydney", "Toronto"
        };
        var departments = new[] { "Engineering", "Research", "Platform", "Data", "Design", "Ops" };

        var rng = new Random(42);
        var rows = new Person[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"{firsts[i % firsts.Length]} {lasts[i / firsts.Length % lasts.Length]}";
            var city = cities[rng.Next(cities.Length)];
            var dept = departments[rng.Next(departments.Length)];
            var salary = 45000m + (rng.Next(0, 8500) * 10m);
            var joined = new DateOnly(2015 + rng.Next(0, 10), 1 + rng.Next(0, 12), 1 + rng.Next(0, 28));
            rows[i] = new Person(i + 1, name, city, dept, salary, joined);
        }

        return rows;
    }

    protected override RenderResult Render()
    {
        var sizeRaw = Size ?? 10;
        var size = sizeRaw is 5 or 10 or 25 or 50 ? sizeRaw : 10;
        var dirAsc = !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortKey = (SortKey ?? string.Empty).ToLowerInvariant();
        var filter = Filter?.Trim() ?? string.Empty;

        // The host does the data work; TableModel never filters, sorts, or slices.
        IEnumerable<Person> view = _people;
        if (filter.Length > 0)
        {
            view = view.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.City.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Department.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        view = sortKey switch
        {
            "id" => dirAsc ? view.OrderBy(p => p.Id) : view.OrderByDescending(p => p.Id),
            "name" => dirAsc ? view.OrderBy(p => p.Name) : view.OrderByDescending(p => p.Name),
            "city" => dirAsc ? view.OrderBy(p => p.City) : view.OrderByDescending(p => p.City),
            "department" => dirAsc ? view.OrderBy(p => p.Department) : view.OrderByDescending(p => p.Department),
            "salary" => dirAsc ? view.OrderBy(p => p.Salary) : view.OrderByDescending(p => p.Salary),
            "joined" => dirAsc ? view.OrderBy(p => p.Joined) : view.OrderByDescending(p => p.Joined),
            _ => view
        };

        var filtered = view.ToArray();
        var totalFiltered = filtered.Length;
        var totalPages = Math.Max(1, (totalFiltered + size - 1) / size);
        var pageRaw = Page ?? 1;
        var page = Math.Clamp(pageRaw < 1 ? 1 : pageRaw, 1, totalPages);
        var visible = filtered.Skip((page - 1) * size).Take(size).ToArray();
        var from = totalFiltered == 0 ? 0 : ((page - 1) * size) + 1;
        var to = totalFiltered == 0 ? 0 : from + visible.Length - 1;

        // Current sort as the controlled-state prop the model echoes back through ctx.Headers.
        IReadOnlyList<ColumnSort> sortState = sortKey.Length > 0
            ? [new ColumnSort(sortKey, dirAsc ? SortDirection.Ascending : SortDirection.Descending)]
            : [];

        return
        [
            PageHeader.Render(
                "Data table",
                "Sortable columns, paged rows, search and page-size selector — all driven from the URL query string. " +
                "Sorting and paging flow through the headless TableModel<T> primitive; search and page size stay plain " +
                "query-param controls."),
            TableModel<Person>(
                ctx => Div(Class: "card shadow-sm border-0")[
                    Div(Class:
                        "card-header bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                        Div(Class: "input-group input-group-sm", Style: "max-width:320px;")[
                            Span(Class: "input-group-text bg-white")[I(Class: "bi bi-search")],
                            Input(
                                "search",
                                Class: "form-control",
                                Placeholder: "Filter name, city, department…",
                                Value: Filter ?? string.Empty,
                                OnInput: v => nav.SetQuery(
                                    KeyValuePair.Create<string, string?>("filter", v ?? string.Empty),
                                    KeyValuePair.Create<string, string?>("page", "1")))
                        ],
                        Div(Class: "d-flex align-items-center gap-2")[
                            Label(Class: "small text-secondary mb-0")["Rows per page"],
                            Select(
                                Class: "form-select form-select-sm",
                                Style: "max-width:90px;",
                                OnChange: v => nav.SetQuery(
                                    KeyValuePair.Create<string, string?>("size", v),
                                    KeyValuePair.Create<string, string?>("page", "1")))[
                                Option("5", size == 5)["5"],
                                Option("10", size == 10)["10"],
                                Option("25", size == 25)["25"],
                                Option("50", size == 50)["50"]
                            ]
                        ]
                    ],
                    Div(Class: "table-responsive")[
                        Table(Class: "table table-hover table-striped align-middle mb-0")[
                            Thead(Class: "table-light")[
                                Tr()[ctx.Headers.Select(SortHeader)]
                            ],
                            Tbody()[
                                ctx.Rows.Count == 0
                                    ? Tr()[
                                        Td(6, Class: "text-center text-secondary py-4")[
                                            I(Class: "bi bi-search me-2"),
                                            "No people match your search."
                                        ]
                                    ]
                                    : Fragment()[
                                        ctx.Rows.Select(row =>
                                            Tr(Key: row.Key)[
                                                Td(Class: "text-secondary")[row.Value.Id],
                                                Td(Class: "fw-semibold")[row.Value.Name],
                                                Td()[row.Value.City],
                                                Td()[
                                                    Span(Class: $"badge {DeptBadge(row.Value.Department)}")[
                                                        row.Value.Department]
                                                ],
                                                Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                                                    "$" + row.Value.Salary.ToString("N0", CultureInfo.InvariantCulture)
                                                ],
                                                Td(Class: "text-secondary small")[
                                                    row.Value.Joined.ToString("yyyy-MM-dd")]
                                            ])
                                    ]
                            ]
                        ]
                    ],
                    Div(Class:
                        "card-footer bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                        Small(Class: "text-secondary")[
                            totalFiltered == 0
                                ? "Showing 0 of 0"
                                : $"Showing {from}–{to} of {totalFiltered}",
                            filter.Length > 0
                                ? Span(Class: "ms-1")[$"(filtered from {_people.Length} total)"]
                                : Fragment()
                        ],
                        Nav()[
                            Ul(Class: "pagination pagination-sm mb-0")[BuildPagination(page, totalPages, ctx.SetPage)]
                        ]
                    ]
                ],
                Columns: _columns,
                Rows: visible,
                KeySelector: p => p.Id,
                Sort: sortState,
                PageIndex: page - 1,
                PageCount: totalPages,
                PageSize: size,
                TotalRowCount: totalFiltered,
                OnSort: OnSortChanged,
                OnPage: OnPageChanged),
            P(Class: "small text-secondary mt-3 mb-0")[
                "Sorting and paging go through ",
                Code()["TableModel<Person>"],
                " — a headless, controlled primitive. It owns no state: this page sorts and slices the data, hands it " +
                "the page of rows plus the current sort/page, and reacts to its ",
                Code()["OnSort"],
                " / ",
                Code()["OnPage"],
                " intents by writing to the URL via ",
                Code()["Navigator.SetQuery"],
                ". The page is then re-resolved against the new query, so browser back / forward replay the state for " +
                "free. Try sorting + paging, then copy the URL — it's shareable."
            ],
            CodeSample(
                ["TablePage.cs"],
                Title: "Source",
                Notes:
                "The whole page above, verbatim. The [QueryParam] properties bind sort, filter, page and " +
                "size from the URL; the host does the filtering, sorting and slicing, then hands the " +
                "headless TableModel<Person> the visible page plus the current sort/page and writes its " +
                "OnSort / OnPage intents back through Navigator.SetQuery.")
        ];
    }

    private void OnSortChanged(IReadOnlyList<ColumnSort> sort)
    {
        var entry = sort.Count > 0 ? sort[0] : (ColumnSort?)null;
        var nextSort = entry?.ColumnId ?? string.Empty;
        var nextDir = entry is { Direction: SortDirection.Descending } ? "desc" : "asc";
        nav.SetQuery(
            KeyValuePair.Create<string, string?>("sort", nextSort),
            KeyValuePair.Create<string, string?>("dir", nextDir),
            KeyValuePair.Create<string, string?>("page", "1"));
    }

    private void OnPageChanged(int pageIndex) => nav.SetQuery("page", (pageIndex + 1).ToString());

    private static Component SortHeader(HeaderCell header)
    {
        var style = header.ColumnId switch
        {
            "id" => "width:80px;",
            "city" => "width:160px;",
            "department" => "width:160px;",
            "salary" => "width:140px; text-align:right;",
            "joined" => "width:140px;",
            _ => null
        };

        var icon = header.Direction switch
        {
            SortDirection.Ascending => "bi-chevron-up",
            SortDirection.Descending => "bi-chevron-down",
            _ => "bi-chevron-expand text-secondary opacity-50"
        };

        var btnClass =
            "btn btn-link p-0 text-decoration-none text-dark fw-semibold d-inline-flex align-items-center gap-1";
        if (header.ColumnId == "salary")
        {
            btnClass += " ms-auto";
        }

        return Th(Style: style, Scope: "col", Key: header.ColumnId)[
            Button("button", Class: btnClass, OnClick: header.ToggleSort)[
                Span()[header.Header],
                I(Class: $"bi {icon} small")
            ]
        ];
    }

    private static List<Child> BuildPagination(int page, int totalPages, Action<int> setPage)
    {
        var items = new List<Child>
        {
            PageItem("«", 1, page == 1, "First", setPage),
            PageItem("‹", Math.Max(1, page - 1), page == 1, "Prev", setPage)
        };

        var windowSize = 5;
        var start = Math.Max(1, page - (windowSize / 2));
        var end = Math.Min(totalPages, start + windowSize - 1);
        start = Math.Max(1, end - windowSize + 1);

        if (start > 1)
        {
            items.Add(NumberItem(1, page, setPage));
            if (start > 2)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }
        }

        for (var i = start; i <= end; i++)
        {
            items.Add(NumberItem(i, page, setPage));
        }

        if (end < totalPages)
        {
            if (end < totalPages - 1)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }

            items.Add(NumberItem(totalPages, page, setPage));
        }

        items.Add(PageItem("›", Math.Min(totalPages, page + 1), page == totalPages, "Next", setPage));
        items.Add(PageItem("»", totalPages, page == totalPages, "Last", setPage));
        return items;
    }

    private static Child PageItem(string glyph, int targetPage, bool disabled, string label, Action<int> setPage)
    {
        return Li(Class: disabled ? "page-item disabled" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                Disabled: disabled,
                OnClick: () => setPage(targetPage - 1))[
                Span(Class: "visually-hidden")[label],
                Span()[glyph]
            ]
        ];
    }

    private static Child NumberItem(int n, int current, Action<int> setPage)
    {
        var active = n == current;
        return Li(Class: active ? "page-item active" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                OnClick: () => setPage(n - 1))[
                n
            ]
        ];
    }

    private static string DeptBadge(string department) => department switch
    {
        "Engineering" => "text-bg-primary",
        "Research" => "text-bg-info",
        "Platform" => "text-bg-success",
        "Data" => "text-bg-warning",
        "Design" => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    private sealed record Person(
        int Id,
        string Name,
        string City,
        string Department,
        decimal Salary,
        DateOnly Joined);
}
