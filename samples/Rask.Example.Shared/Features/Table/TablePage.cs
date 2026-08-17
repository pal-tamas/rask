using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// A list screen whose entire UI state — search filter, sort column and direction, current page and page size —
// lives in the URL as [QueryParam] properties. BsDataGrid renders the table and reports what the user clicked;
// this page decides what that means and writes it back through Navigator.SetQuery. Rask re-resolves the page
// against the new query, so the state is shareable, bookmarkable, and replayed by browser back/forward for free.
public sealed partial class TablePage(Navigator nav) : Page
{
    protected override string Route => "table";

    protected override Type? Parent => typeof(ShowcaseLayout);

    private static readonly Person[] _people = BuildPeople(120);

    [QueryParam] public string? Filter { get; set; }
    [QueryParam("sort")] public string? SortKey { get; set; }
    [QueryParam("dir")] public string? Dir { get; set; }
    [QueryParam] public int? Page { get; set; }
    [QueryParam] public int? Size { get; set; }

    protected override Component? HeadAssets => Title["Data table — Rask"];

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

        // Filtering stays the page's job — it is a query concern, not a grid one.
        var rows = _people.AsEnumerable();
        if (filter.Length > 0)
        {
            rows = rows.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.City.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Department.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = rows.ToArray();

        return
        [
            PageHeader
                .Title("Data table")
                .Lead("Sortable columns, paged rows, search and a page-size selector — a BsDataGrid whose sort and " +
                      "page are owned by the URL query string."),
            BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
                BsCardHeader.Class("bg-white d-flex flex-wrap gap-2 align-items-center justify-content-between")[
                    Div.Class("input-group input-group-sm").Style("max-width:320px;")[
                        Span.Class("input-group-text bg-white")[BsIcon.Name(BsIconName.Search)],
                        Input
                            .Value(Filter ?? string.Empty)
                            .Type(InputType.Search)
                            .Class("form-control")
                            .Placeholder("Filter name, city, department…")
                            .OnInput(v => nav.SetQuery(
                                KeyValuePair.Create<string, string?>("filter", v ?? string.Empty),
                                KeyValuePair.Create<string, string?>("page", "1")))
                    ],
                    BsStack.Gap(2).Align(BsAlign.Center)[
                        Label.Class("small text-secondary mb-0")["Rows per page"],
                        Select.Value<string>(null)
                            .Class("form-select form-select-sm")
                            .Style("max-width:90px;")
                            .OnChange(v => nav.SetQuery(
                                KeyValuePair.Create<string, string?>("size", v),
                                KeyValuePair.Create<string, string?>("page", "1")))[
                            Option.Value("5").Selected(size == 5)["5"],
                            Option.Value("10").Selected(size == 10)["10"],
                            Option.Value("25").Selected(size == 25)["25"],
                            Option.Value("50").Selected(size == 50)["50"]
                        ]
                    ]
                ],
                BsCardBody.Class("pt-0")[
                    // Page and Sort are the URL's, so the grid renders what the query says and reports clicks
                    // instead of moving itself. Page is 0-based here and 1-based in the URL (nicer to share).
                    BsDataGrid
                        .Data(filtered)
                        .Columns(BuildColumns())
                        .Id("people-grid")
                        .PageSize(size)
                        .RowKey(p => p.Id)
                        .Class("align-middle")
                        .Page(Math.Max(0, (Page ?? 1) - 1))
                        .OnPageChange(p => nav.SetQuery("page", (p + 1).ToString(CultureInfo.InvariantCulture)))
                        .Sort(sortKey.Length == 0 ? null : sortKey)
                        .SortDescending(!dirAsc)
                        .OnSortChange(s => CycleSort(s.Field, sortKey, dirAsc))
                        .Empty(Div.Class("text-center text-secondary py-4")[
                            BsIcon.Name(BsIconName.Search).Class("me-2"),
                            "No people match your search."
                        ])
                ]
            ],
            filter.Length > 0
                ? P.Class("small text-secondary mt-2 mb-0")[
                    $"Filtered from {_people.Length} total."]
                : null,
            P.Class("small text-secondary mt-3 mb-0")[
                "The page holds every bit of UI state in ",
                Code["[QueryParam]"],
                " properties and writes each header click and pager button back through ",
                Code["Navigator.SetQuery"],
                ". The page is re-resolved against the new query, so browser back / forward replay the state for " +
                "free. Try sorting + paging, then copy the URL — it's shareable."
            ],
            CodeSample
                .Files(["TablePage.cs"])
                .Title("Source")
                .Notes("The whole page above, verbatim. The [QueryParam] properties bind sort, filter, page and size " +
                "from the URL. BsDataGrid takes Page/Sort as inputs and raises OnPageChange/OnSortChange instead " +
                "of tracking them itself, so the URL — not the component — is the single source of truth.")
        ];
    }

    // asc → desc → unsorted, then back to asc on the next column. The grid only reports the click, so this
    // three-state cycle is the page's to define.
    private void CycleSort(string? field, string currentSort, bool currentAsc)
    {
        var (nextSort, nextDir) =
            !string.Equals(currentSort, field, StringComparison.Ordinal) ? (field ?? "", "asc")
            : currentAsc ? (field ?? "", "desc")
            : ("", "asc"); // desc → cleared

        nav.SetQuery(
            KeyValuePair.Create<string, string?>("sort", nextSort),
            KeyValuePair.Create<string, string?>("dir", nextDir),
            KeyValuePair.Create<string, string?>("page", "1"));
    }

    // SortField is what the URL carries and what OnSortChange reports back; SortKey is what the in-memory sort
    // orders by. Both are needed: one names the column, the other knows how to compare it.
    private static BsColumn<Person>[] BuildColumns() =>
    [
        new BsColumn<Person>
        {
            Title = "#", Sortable = true, SortField = "id", SortKey = p => p.Id,
            Value = p => p.Id, Class = "text-secondary",
        },
        new BsColumn<Person>
        {
            Title = "Name", Sortable = true, SortField = "name", SortKey = p => p.Name,
            Value = p => p.Name, Class = "fw-semibold",
        },
        new BsColumn<Person>
        {
            Title = "City", Sortable = true, SortField = "city", SortKey = p => p.City, Value = p => p.City,
        },
        new BsColumn<Person>
        {
            Title = "Department", Sortable = true, SortField = "department", SortKey = p => p.Department,
            Template = p => Span.Class($"badge {DeptBadge(p.Department)}")[p.Department],
        },
        new BsColumn<Person>
        {
            Title = "Salary", Sortable = true, SortField = "salary", SortKey = p => p.Salary,
            Class = "text-end",
            Value = p => "$" + p.Salary.ToString("N0", CultureInfo.InvariantCulture),
        },
        new BsColumn<Person>
        {
            Title = "Joined", Sortable = true, SortField = "joined", SortKey = p => p.Joined,
            Class = "text-secondary small", Value = p => p.Joined.ToString("yyyy-MM-dd"),
        },
    ];

    private static string DeptBadge(string department) => department switch
    {
        "Engineering" => "text-bg-primary",
        "Research" => "text-bg-info",
        "Platform" => "text-bg-success",
        "Data" => "text-bg-warning",
        "Design" => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    private sealed record Person(int Id, string Name, string City, string Department, decimal Salary,
        DateOnly Joined);
}
