namespace Rask.Example.Shared.Features;

// A server-side-paged BsDataGrid<T> with a busy state: Page/Sort are controlled, OnPageChangeAsync and
// OnSortChangeAsync await the "query", and Loading is set around it so the grid dims behind a spinner and
// refuses further clicks until the rows land.
//
// The delay here is artificial (a real app awaits CountAsync/ToListAsync); everything else is exactly the
// shape a database-backed grid uses. Fetch() stands in for the query: it sorts and slices the source and
// reports the total, which is what TotalCount needs to size the pager.
//
// Loading is bool?, and all three states are used: null would mean "not using the feature at all", so this
// demo starts at false (in use, idle) and flips to true around the await.
public sealed partial class BsDataGridLoadingDemo : Component
{
    private const int PageSize = 4;

    private sealed record City(string Name, string Country, int Population);

    private static readonly List<City> Source =
    [
        new("Tokyo", "Japan", 37_400_068),
        new("Delhi", "India", 32_226_000),
        new("Shanghai", "China", 29_211_000),
        new("Dhaka", "Bangladesh", 23_210_000),
        new("Sao Paulo", "Brazil", 22_430_000),
        new("Cairo", "Egypt", 22_183_000),
        new("Mexico City", "Mexico", 22_085_000),
        new("Beijing", "China", 21_766_000),
        new("Mumbai", "India", 21_296_000),
        new("Osaka", "Japan", 19_013_000),
        new("Karachi", "Pakistan", 17_236_000),
        new("Istanbul", "Turkiye", 15_848_000),
    ];

    private List<City> _rows = Fetch(0, null, false).Rows;
    private bool _loading;
    private int _page;
    private string? _sort;
    private bool _sortDescending;
    private int _total = Source.Count;

    protected override Component? Render() =>
        Div.Id("grid-loading-demo")[
            BsDataGrid
                .Data(_rows)
                .Columns([
                    new BsColumn<City> { Title = "City", Value = c => c.Name, Sortable = true, SortField = "name" },
                    new BsColumn<City>
                    {
                        Title = "Country", Value = c => c.Country, Sortable = true, SortField = "country",
                    },
                    new BsColumn<City>
                    {
                        Title = "Population", Class = Txt.End(), Sortable = true, SortField = "pop",
                        Value = c => c.Population.ToString("N0"),
                    },
                ])
                .Id("bs-grid-loading")
                .TotalCount(_total)
                .PageSize(PageSize)
                .RowKey(c => c.Name)
                .Loading(_loading)
                .Page(_page)
                .Sort(_sort)
                .SortDescending(_sortDescending)
                .OnPageChangeAsync(async page =>
                {
                    _page = page;
                    await ReloadAsync();
                })
                .OnSortChangeAsync(async sort =>
                {
                    _sort = sort.Field;
                    _sortDescending = sort.Descending;
                    // A controlled sort owns the page too: re-sorting and staying on page 3 would show rows
                    // nobody asked for.
                    _page = 0;
                    await ReloadAsync();
                })];

    // Set Loading, await, clear it — and note there is no StateHasChanged anywhere.
    //
    // Rask renders this component twice for free: once when the await actually yields (which is what paints
    // the spinner) and once when the handler returns (which clears it). Neither needs asking for.
    private async Task ReloadAsync()
    {
        _loading = true;

        await Task.Delay(600); // stands in for CountAsync/ToListAsync

        var (rows, total) = Fetch(_page, _sort, _sortDescending);
        _rows = rows;
        _total = total;
        _loading = false;
    }

    // Stands in for the database: order, count, then take one page — the same work an IQueryable would do in
    // the store, which is why the grid only ever sees a slice plus a TotalCount.
    private static (List<City> Rows, int Total) Fetch(int page, string? sort, bool descending)
    {
        IEnumerable<City> query = Source;

        query = sort switch
        {
            "name" => descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "country" => descending ? query.OrderByDescending(c => c.Country) : query.OrderBy(c => c.Country),
            "pop" => descending
                ? query.OrderByDescending(c => c.Population)
                : query.OrderBy(c => c.Population),
            _ => query,
        };

        return (query.Skip(page * PageSize).Take(PageSize).ToList(), Source.Count);
    }
}
