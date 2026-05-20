using System.Globalization;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("table")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TablePage(Navigator nav) : Component
{
    private static readonly Person[] _people = BuildPeople(120);

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

    protected override Component Render()
    {
        var sizeRaw = Size ?? 10;
        var size = sizeRaw is 5 or 10 or 25 or 50 ? sizeRaw : 10;
        var dirAsc = !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortKey = (SortKey ?? string.Empty).ToLowerInvariant();
        var filter = Filter?.Trim() ?? string.Empty;

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

        return Fragment()[
            PageHeader.Render(
                "Data table",
                "Sortable columns, paged rows, search and page-size selector — all driven from the URL query string. Bookmark or share any view."),
            Div(Class: "card shadow-sm border-0")[
                Div(Class: "card-header bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
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
                            Tr()[
                                SortHeader("id", "#", "width:80px;", sortKey, dirAsc),
                                SortHeader("name", "Name", null, sortKey, dirAsc),
                                SortHeader("city", "City", "width:160px;", sortKey, dirAsc),
                                SortHeader("department", "Department", "width:160px;", sortKey, dirAsc),
                                SortHeader("salary", "Salary", "width:140px; text-align:right;", sortKey, dirAsc, true),
                                SortHeader("joined", "Joined", "width:140px;", sortKey, dirAsc)
                            ]
                        ],
                        Tbody()[
                            totalFiltered == 0
                                ? (Child)Tr()[
                                    Td(6, Class: "text-center text-secondary py-4")[
                                        I(Class: "bi bi-search me-2"),
                                        "No people match your search."
                                    ]
                                ]
                                : (Child)Fragment()[
                                    visible.Select(p =>
                                        (Child)Tr()[
                                            Td(Class: "text-secondary")[p.Id.ToString()],
                                            Td(Class: "fw-semibold")[p.Name],
                                            Td()[p.City],
                                            Td()[
                                                Span(Class: $"badge {DeptBadge(p.Department)}")[p.Department]
                                            ],
                                            Td(Style: "text-align:right; font-variant-numeric:tabular-nums;")[
                                                "$" + p.Salary.ToString("N0", CultureInfo.InvariantCulture)
                                            ],
                                            Td(Class: "text-secondary small")[p.Joined.ToString("yyyy-MM-dd")]
                                        ])
                                ]
                        ]
                    ]
                ],
                Div(Class: "card-footer bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                    Small(Class: "text-secondary")[
                        totalFiltered == 0
                            ? "Showing 0 of 0"
                            : $"Showing {from}–{to} of {totalFiltered}",
                        filter.Length > 0
                            ? (Child)Span(Class: "ms-1")[$"(filtered from {_people.Length} total)"]
                            : (Child)Fragment()
                    ],
                    Nav()[
                        Ul(Class: "pagination pagination-sm mb-0")[BuildPagination(page, totalPages)]
                    ]
                ]
            ],
            P(Class: "small text-secondary mt-3 mb-0")[
                "Every interaction writes to the URL via ",
                Code()["Navigator.SetQuery"],
                ". The page is then re-resolved against the new query: ",
                Code()["[QueryParam]"],
                " properties are re-bound by ",
                Code()["PageBinder"],
                ", and browser back / forward replay the state for free. Try sorting + paging, then copy the URL — it's shareable. (Filter input pushes a history entry per keystroke; production code would debounce or use ",
                Code()["Navigate(…, replace: true)"],
                ".)"
            ]
        ];
    }

    private Component SortHeader(string key, string label, string? style, string sortKey, bool dirAsc,
        bool rightAlign = false)
    {
        var active = sortKey == key;
        var activeAsc = active && dirAsc;
        var activeDesc = active && !dirAsc;

        string icon;
        if (!active)
        {
            icon = "bi-chevron-expand text-secondary opacity-50";
        }
        else if (dirAsc)
        {
            icon = "bi-chevron-up";
        }
        else
        {
            icon = "bi-chevron-down";
        }

        string nextSort;
        string nextDir;
        if (activeDesc)
        {
            nextSort = string.Empty;
            nextDir = "asc";
        }
        else if (activeAsc)
        {
            nextSort = key;
            nextDir = "desc";
        }
        else
        {
            nextSort = key;
            nextDir = "asc";
        }

        var btnClass =
            "btn btn-link p-0 text-decoration-none text-dark fw-semibold d-inline-flex align-items-center gap-1";
        if (rightAlign)
        {
            btnClass += " ms-auto";
        }

        return Th(Style: style, Scope: "col")[
            Button(
                "button",
                Class: btnClass,
                OnClick: () => nav.SetQuery(
                    KeyValuePair.Create<string, string?>("sort", nextSort),
                    KeyValuePair.Create<string, string?>("dir", nextDir),
                    KeyValuePair.Create<string, string?>("page", "1")))[
                Span()[label],
                I(Class: $"bi {icon} small")
            ]
        ];
    }

    private List<Child> BuildPagination(int page, int totalPages)
    {
        var items = new List<Child>
        {
            PageItem("«", 1, page == 1, "First"), PageItem("‹", Math.Max(1, page - 1), page == 1, "Prev")
        };

        var windowSize = 5;
        var start = Math.Max(1, page - (windowSize / 2));
        var end = Math.Min(totalPages, start + windowSize - 1);
        start = Math.Max(1, end - windowSize + 1);

        if (start > 1)
        {
            items.Add(NumberItem(1, page));
            if (start > 2)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }
        }

        for (var i = start; i <= end; i++)
        {
            items.Add(NumberItem(i, page));
        }

        if (end < totalPages)
        {
            if (end < totalPages - 1)
            {
                items.Add(Li(Class: "page-item disabled")[Span(Class: "page-link")["…"]]);
            }

            items.Add(NumberItem(totalPages, page));
        }

        items.Add(PageItem("›", Math.Min(totalPages, page + 1), page == totalPages, "Next"));
        items.Add(PageItem("»", totalPages, page == totalPages, "Last"));
        return items;
    }

    private Child PageItem(string glyph, int targetPage, bool disabled, string label)
    {
        return Li(Class: disabled ? "page-item disabled" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                Disabled: disabled,
                OnClick: () => nav.SetQuery("page", targetPage.ToString()))[
                Span(Class: "visually-hidden")[label],
                Span()[glyph]
            ]
        ];
    }

    private Child NumberItem(int n, int current)
    {
        var active = n == current;
        return Li(Class: active ? "page-item active" : "page-item")[
            Button(
                "button",
                Class: "page-link",
                OnClick: () => nav.SetQuery("page", n.ToString()))[
                n.ToString()
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
