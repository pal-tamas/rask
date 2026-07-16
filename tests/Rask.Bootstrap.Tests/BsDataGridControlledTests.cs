using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

#pragma warning disable RASK014 // test-defined Component subclasses are constructed directly

namespace Rask.Bootstrap.Tests;

// Caller-owned state: a controlled Page/Sort (the grid reports what the user clicked instead of moving itself)
// and TotalCount (Data is one already-paged slice from a query). Together these are server-side paging and
// sorting, so the tests assert what the grid REPORTS as much as what it renders.
public class BsDataGridControlledTests
{
    private sealed record Row(string Name, int Qty);

    // Renders the grid from a parent, so the generated factory runs inside the live render context and the
    // grid gets its mount lifecycle (which is what asks the provider for the first page). Constructing the
    // factory straight into RaskTest.Render bypasses that and the grid never fetches.
    private sealed class Host(Func<Component> build) : Component
    {
        protected override Component? Render() => build();
    }

    private static readonly List<Row> All =
    [
        new("Banana", 3),
        new("Apple", 5),
        new("Cherry", 1),
        new("Date", 9),
        new("Elderberry", 7),
    ];

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true, SortField = "name" },
        new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Sortable = true, SortField = "qty" },
    ];

    private static string[] BodyCells(string html, int column)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(r => Regex.Matches(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline))
            .Where(cells => cells.Count > column)
            .Select(cells => cells[column].Groups[1].Value)
            .ToArray();
    }

    [Fact]
    public void ControlledPage_RendersTheCallersPage_AndReportsClicksInsteadOfMoving()
    {
        var asked = new List<int>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(), PageSize: 2,
            Page: 1, OnPageChange: p => asked.Add(p))));

        // The caller said page 2, so that is what renders.
        Assert.Equal(["Cherry", "Date"], BodyCells(grid.Html, 0));
        Assert.Contains("3-4 / 5", grid.Html);
    }

    [Fact]
    public async Task ControlledPage_ReportsTheRequestedPage_AndDoesNotMoveItself()
    {
        var asked = new List<int>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(), PageSize: 2,
            Page: 0, OnPageChange: p => asked.Add(p))));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[4]); // page 2

        Assert.Equal([1], asked);
        // Still page 1: the grid does not own the page, so nothing moves until the caller re-renders it.
        Assert.Equal(["Banana", "Apple"], BodyCells(html, 0));
    }

    [Fact]
    public void ControlledSort_RendersTheCallersSort()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            Sort: "name", SortDescending: true, OnSortChange: _ => { })));

        Assert.Equal(["Elderberry", "Date", "Cherry", "Banana", "Apple"], BodyCells(grid.Html, 0));
        Assert.Contains("aria-sort=\"descending\"", grid.Html);
        Assert.Contains("bi-caret-down-fill", grid.Html);
    }

    [Fact]
    public async Task ControlledSort_ReportsTheFieldAndDirection_AndFlipsTheActiveColumn()
    {
        var asked = new List<DataGridSort>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            Sort: "name", SortDescending: false, OnSortChange: s => asked.Add(s))));

        // Clicking the already-sorted column asks for the opposite direction...
        await grid.InvokeAsync(grid.HandlerIds("click")[0]);
        Assert.Equal(new DataGridSort("name", true), asked[^1]);

        // ...and a different column asks for ascending on that field.
        await grid.InvokeAsync(grid.HandlerIds("click")[1]);
        Assert.Equal(new DataGridSort("qty", false), asked[^1]);
    }

    [Fact]
    public async Task ControlledSort_DoesNotSortItself()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            Sort: null, OnSortChange: _ => { })));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]);

        // The caller owns the sort and has not changed it, so the order is untouched.
        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], BodyCells(html, 0));
        Assert.DoesNotContain("aria-sort=\"ascending\"", html);
    }

    [Fact]
    public async Task WithoutTotalCount_TheGridSortsAndPagesInMemory()
    {
        // TotalCount is purely opt-in: leave it off and the grid owns the whole list exactly as it always has —
        // it sorts Data itself, slices it itself, and sizes the pager from Data.Count.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(), PageSize: 2)));

        Assert.Equal(["Banana", "Apple"], BodyCells(grid.Html, 0));
        Assert.Contains("1-2 / 5", grid.Html); // the whole list, not the page

        // It sorts...
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]);
        Assert.Equal(["Apple", "Banana"], BodyCells(html, 0));

        // ...and pages, entirely on its own.
        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[4]);
        Assert.Equal(["Cherry", "Date"], BodyCells(html, 0));
        Assert.Contains("3-4 / 5", html);
    }

    [Fact]
    public void Sort_WithoutAnyCallback_IsStillHonoured()
    {
        // Sort = null means "unsorted", so it cannot signal intent the way a non-null Page does. Passing Sort
        // alone therefore has to count as opting in — otherwise the grid would silently ignore it.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            Sort: "name", SortDescending: true)));

        Assert.Equal(["Elderberry", "Date", "Cherry", "Banana", "Apple"], BodyCells(grid.Html, 0));
        Assert.Contains("aria-sort=\"descending\"", grid.Html);
    }

    [Fact]
    public async Task OnSortChangeAsync_IsAwaited_SoTheHandlerCanRunAnAsyncQuery()
    {
        // This is how the grid supports async data with no async machinery of its own: the click awaits the
        // handler, which is where CountAsync/ToListAsync would run before updating Data + TotalCount.
        var asked = new List<DataGridSort>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            Sort: "name", SortDescending: false,
            OnSortChangeAsync: async s =>
            {
                await Task.Yield();
                asked.Add(s);
            })));

        await grid.InvokeAsync(grid.HandlerIds("click")[0]);

        Assert.Equal(new DataGridSort("name", true), Assert.Single(asked));
    }

    [Fact]
    public async Task OnPageChangeAsync_IsAwaited()
    {
        var asked = new List<int>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(), PageSize: 2,
            Page: 0,
            OnPageChangeAsync: async p =>
            {
                await Task.Yield();
                asked.Add(p);
            })));

        await grid.InvokeAsync(grid.HandlerIds("click")[4]); // page 2

        Assert.Equal([1], asked);
    }

    [Fact]
    public async Task AsyncOnlySortCallback_StillTakesControlOfTheSort()
    {
        // The async half alone must opt into controlled mode, exactly as the sync half does.
        var asked = new List<DataGridSort>();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(Data: All, Columns: Columns(),
            OnSortChangeAsync: s =>
            {
                asked.Add(s);
                return Task.CompletedTask;
            })));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]);

        Assert.Equal(new DataGridSort("name", false), Assert.Single(asked));
        // Controlled: it reported the click and left the order alone.
        Assert.Equal(["Banana", "Apple", "Cherry", "Date", "Elderberry"], BodyCells(html, 0));
    }

    [Fact]
    public void TotalCount_TreatsDataAsOneSlice_AndSizesThePagerFromTheWholeSet()
    {
        // Server-side paging: Data is the 2 rows the query returned for page 2; TotalCount says there are 5.
        // Without TotalCount the grid would size the pager from the slice and render a single page.
        var slice = new List<Row> { All[2], All[3] };

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: slice, TotalCount: 5, Columns: Columns(), PageSize: 2, Page: 1, OnPageChange: _ => { })));

        Assert.Equal(["Cherry", "Date"], BodyCells(grid.Html, 0));
        Assert.Contains("3-4 / 5", grid.Html);
        // 5 rows over pages of 2 is 3 pages, even though only 2 rows were handed over.
        Assert.Contains(">3</button>", grid.Html);
    }

    [Fact]
    public void TotalCount_RendersTheSliceVerbatim_WithoutReSortingIt()
    {
        // The query already ordered the rows; re-sorting them in memory would fight it.
        var slice = new List<Row> { All[3], All[1] }; // Date, Apple — a deliberate non-alphabetical order

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: slice, TotalCount: 5, Columns: Columns(), PageSize: 2,
            Sort: "name", SortDescending: false, OnSortChange: _ => { })));

        Assert.Equal(["Date", "Apple"], BodyCells(grid.Html, 0));
        // It still reports the sort it was given.
        Assert.Contains("aria-sort=\"ascending\"", grid.Html);
    }

    [Fact]
    public void TotalCount_OfZero_RendersTheEmptyState()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: [], TotalCount: 0, Columns: Columns(), PageSize: 2, Empty: Div()["nothing"])));

        Assert.Contains("<div>nothing</div>", grid.Html);
        Assert.DoesNotContain("<table", grid.Html);
    }

    [Fact]
    public async Task ControlledPageAndSort_ComposeWithTotalCount()
    {
        // The whole server-side shape: the URL owns page+sort, the query owns the rows, and the grid only
        // reports what the user clicked.
        var asked = new List<DataGridSort>();
        var pages = new List<int>();
        var slice = new List<Row> { All[2], All[3] };

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: slice, TotalCount: 5, Columns: Columns(), PageSize: 2,
            Page: 1, OnPageChange: p => pages.Add(p),
            Sort: "qty", SortDescending: true, OnSortChange: s => asked.Add(s))));

        await grid.InvokeAsync(grid.HandlerIds("click")[0]); // click Name
        Assert.Equal(new DataGridSort("name", false), asked[^1]);

        // Nothing moved on its own: the caller re-renders the grid with the new query results.
        Assert.Equal(["Cherry", "Date"], BodyCells(grid.Html, 0));
        Assert.Empty(pages);
    }

    [Fact]
    public void SortableColumn_WithoutASortField_IsNotSortable_WhenTheSortIsControlled()
    {
        // A controlled sort is reported by SortField; offering a control that cannot be honoured would be a lie.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Sortable = true, SortField = "qty" },
        ];

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All, Columns: columns, PageSize: 2, Sort: null, OnSortChange: _ => { })));

        Assert.Contains("<th scope=\"col\">Name</th>", grid.Html);
        Assert.Single(Regex.Matches(grid.Html, "aria-sort"));
    }
}
