using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("table")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TablePage(Navigator nav) : Component
{
    private static readonly Person[] _people = BuildPeople(120);

    // Plain column metadata (id used for the sort query + width styling, header is the label).
    private static readonly (string Id, string Header)[] _columns =
    [
        ("id", "#"),
        ("name", "Name"),
        ("city", "City"),
        ("department", "Department"),
        ("salary", "Salary"),
        ("joined", "Joined")
    ];

    [QueryParam] public string? Filter { get; set; }
    [QueryParam("sort")] public string? SortKey { get; set; }
    [QueryParam("dir")] public string? Dir { get; set; }
    [QueryParam] public int? Page { get; set; }
    [QueryParam] public int? Size { get; set; }

    protected override Component? Head => Title()["Data table — Rask"];

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

    protected override Component? Render()
    {
        var sizeRaw = Size ?? 10;
        var size = sizeRaw is 5 or 10 or 25 or 50 ? sizeRaw : 10;
        var dirAsc = !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortKey = (SortKey ?? string.Empty).ToLowerInvariant();
        var filter = Filter?.Trim() ?? string.Empty;

        // The page does all the data work itself: filter → sort → slice a page of plain rows.
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

        // Click a header to cycle its sort (asc → desc → none) and reset to page 1, all via the URL.
        void ToggleSort(string columnId)
        {
            var (nextSort, nextDir) =
                !string.Equals(sortKey, columnId, StringComparison.Ordinal) ? (columnId, "asc")
                : dirAsc ? (columnId, "desc")
                : ("", "asc"); // desc → cleared

            nav.SetQuery(
                KeyValuePair.Create<string, string?>("sort", nextSort),
                KeyValuePair.Create<string, string?>("dir", nextDir),
                KeyValuePair.Create<string, string?>("page", "1"));
        }

        void GoToPage(int target) => nav.SetQuery("page", Math.Clamp(target, 1, totalPages).ToString());

        return
        [
            PageHeader.Render(
                "Data table",
                "Sortable columns, paged rows, search and a page-size selector — all rendered with the plain " +
                "Table component and driven from the URL query string."),
            BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
                BsCardHeader(Class: "bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                    Div(Class: "input-group input-group-sm", Style: "max-width:320px;")[
                        Span(Class: "input-group-text bg-white")[BsIcon(Name: BsIconName.Search)],
                        Input(
                            InputType.Search,
                            Class: "form-control",
                            Placeholder: "Filter name, city, department…",
                            Value: Filter ?? string.Empty,
                            OnInput: v => nav.SetQuery(
                                KeyValuePair.Create<string, string?>("filter", v ?? string.Empty),
                                KeyValuePair.Create<string, string?>("page", "1")))
                    ],
                    Div(Class: "d-flex align-items-center gap-2")[
                        Label(Class: "small text-secondary mb-0")["Rows per page"],
                        Select<string>(
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
                            Tr()[_columns.Select(c => SortHeader(c.Id, c.Header, sortKey, dirAsc, ToggleSort))]
                        ],
                        Tbody()[
                            visible.Length == 0
                                ? Tr()[
                                    Td(6, Class: "text-center text-secondary py-4")[
                                        BsIcon(Name: BsIconName.Search, Class: "me-2"),
                                        "No people match your search."
                                    ]
                                ]
                                :
                                [
                                    .. visible.Select(p =>
                                        Tr(Key: p.Id)[
                                            Td(Class: "text-secondary")[p.Id],
                                            Td(Class: "fw-semibold")[p.Name],
                                            Td()[p.City],
                                            Td()[Span(Class: $"badge {DeptBadge(p.Department)}")[p.Department]],
                                            Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                                                "$" + p.Salary.ToString("N0", CultureInfo.InvariantCulture)
                                            ],
                                            Td(Class: "text-secondary small")[p.Joined.ToString("yyyy-MM-dd")]
                                        ])
                                ]
                        ]
                    ]
                ],
                BsCardFooter(Class: "bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                    Small(Class: "text-secondary")[
                        totalFiltered == 0
                            ? "Showing 0 of 0"
                            : $"Showing {from}–{to} of {totalFiltered}",
                        filter.Length > 0
                            ? Span(Class: "ms-1")[$"(filtered from {_people.Length} total)"]
                            : null
                    ],
                    Nav()[
                        Ul(Class: "pagination pagination-sm mb-0")[BuildPagination(page, totalPages, GoToPage)]
                    ]
                ]
            ],
            P(Class: "small text-secondary mt-3 mb-0")[
                "The page renders a plain ",
                Code()["Table"],
                " and owns all the state itself: it filters, sorts and slices the data, then writes the current " +
                "sort and page back to the URL via ",
                Code()["Navigator.SetQuery"],
                ". The page is re-resolved against the new query, so browser back / forward replay the state for " +
                "free. Try sorting + paging, then copy the URL — it's shareable."
            ],
            CodeSample(
                ["TablePage.cs"],
                Title: "Source",
                Notes:
                "The whole page above, verbatim. The [QueryParam] properties bind sort, filter, page and size from " +
                "the URL; the host does the filtering, sorting and slicing and renders the rows straight into a " +
                "Table, writing each header click and pager button back through Navigator.SetQuery.")
        ];
    }

    private static Component SortHeader(string columnId, string header, string sortKey, bool dirAsc,
        Action<string> toggleSort)
    {
        var style = columnId switch
        {
            "id" => "width:80px;",
            "city" => "width:160px;",
            "department" => "width:160px;",
            "salary" => "width:140px; text-align:right;",
            "joined" => "width:140px;",
            _ => null
        };

        var sorted = string.Equals(sortKey, columnId, StringComparison.Ordinal);
        var icon = sorted
            ? dirAsc ? "bi-chevron-up" : "bi-chevron-down"
            : "bi-chevron-expand text-secondary opacity-50";

        var btnClass =
            "btn btn-link p-0 text-decoration-none text-dark fw-semibold d-inline-flex align-items-center gap-1";
        if (columnId == "salary")
        {
            btnClass += " ms-auto";
        }

        return Th(Style: style, Scope: "col", Key: columnId)[
            Button("button", Class: btnClass, OnClick: () => toggleSort(columnId))[
                Span()[header],
                I(Class: $"bi {icon} small")
            ]
        ];
    }

    private static List<Component> BuildPagination(int page, int totalPages, Action<int> goToPage)
    {
        var items = new List<Component>
        {
            PageItem("«", 1, page == 1, "First", goToPage),
            PageItem("‹", Math.Max(1, page - 1), page == 1, "Prev", goToPage)
        };

        var windowSize = 5;
        var start = Math.Max(1, page - (windowSize / 2));
        var end = Math.Min(totalPages, start + windowSize - 1);
        start = Math.Max(1, end - windowSize + 1);

        if (start > 1)
        {
            items.Add(NumberItem(1, page, goToPage));
            if (start > 2)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }
        }

        for (var i = start; i <= end; i++)
        {
            items.Add(NumberItem(i, page, goToPage));
        }

        if (end < totalPages)
        {
            if (end < totalPages - 1)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }

            items.Add(NumberItem(totalPages, page, goToPage));
        }

        items.Add(PageItem("›", Math.Min(totalPages, page + 1), page == totalPages, "Next", goToPage));
        items.Add(PageItem("»", totalPages, page == totalPages, "Last", goToPage));
        return items;
    }

    private static Component PageItem(string glyph, int targetPage, bool disabled, string label, Action<int> goToPage)
    {
        return Li(Class: disabled ? "page-item disabled" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                Disabled: disabled,
                OnClick: () => goToPage(targetPage))[
                Span(Class: "visually-hidden")[label],
                Span()[glyph]
            ]
        ];
    }

    private static Component NumberItem(int n, int current, Action<int> goToPage)
    {
        var active = n == current;
        return Li(Class: active ? "page-item active" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                OnClick: () => goToPage(n))[
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
