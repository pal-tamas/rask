using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

#pragma warning disable RASK014 // test-defined Component subclasses are constructed directly

namespace Rask.Bootstrap.Tests;

// Passing an IQueryable as Data: the grid orders, counts and pages it in the store rather than in memory.
// These use a LINQ-to-Objects queryable, which exercises the same Queryable.OrderBy/Count/Skip/Take
// composition an EF provider translates to SQL; the real-SQL proof is the EF Core sample.
public partial class BsDataGridQueryTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(string Name, int Qty);

    private static readonly List<Row> All =
    [
        new("Banana", 3),
        new("Apple", 5),
        new("Cherry", 1),
        new("Date", 9),
        new("Elderberry", 7),
    ];

    private sealed class Host(Func<Component> build) : Component
    {
        protected override Component? Render() => build();
    }

    // Counts how often the query is actually executed, so the cache can be asserted rather than assumed. The
    // counter sits in the underlying sequence because Count()/Skip()/Take() run through the query PROVIDER —
    // wrapping IQueryable.GetEnumerator would never see them.
    private sealed class CountingSource
    {
        public int Executions { get; private set; }

        public IEnumerable<Row> Rows()
        {
            Executions++;
            foreach (var row in All)
            {
                yield return row;
            }
        }
    }

    private static BsColumn<Row>[] Columns() =>
    [
        new BsColumn<Row>
        {
            Title = "Name", Value = r => r.Name, Sortable = true, SortField = "name", SortBy = r => r.Name,
        },
        new BsColumn<Row>
        {
            Title = "Qty", Value = r => r.Qty, Sortable = true, SortField = "qty", SortBy = r => r.Qty,
        },
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
    public void Query_PagesFromTheStore_AndCountsTheWholeSet()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 2)));

        Assert.Equal(["Banana", "Apple"], BodyCells(grid.Html, 0));
        // The pager is sized from Count() over the whole query, not from the materialised page.
        Assert.Contains("1-2 / 5", grid.Html);
        Assert.Contains(">3</button>", grid.Html);
    }

    [Fact]
    public async Task Query_SortsInTheStore_ViaSortBy()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 2)));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[0]); // by Name
        Assert.Equal(["Apple", "Banana"], BodyCells(html, 0));
        Assert.Contains("aria-sort=\"ascending\"", html);

        html = await grid.InvokeAsync(Markup.Attrs(html, "data-rask-on-click")[0]); // descending
        Assert.Equal(["Elderberry", "Date"], BodyCells(html, 0));
        Assert.Contains("aria-sort=\"descending\"", html);
    }

    [Fact]
    public async Task Query_SortsByTheValueNotItsText()
    {
        // SortBy is an expression over the property, so ordering is numeric even though the cell renders text.
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 5)));

        var html = await grid.InvokeAsync(grid.HandlerIds("click")[1]); // by Qty

        Assert.Equal(["Cherry", "Banana", "Apple", "Elderberry", "Date"], BodyCells(html, 0)); // 1,3,5,7,9
    }

    [Fact]
    public async Task Query_PagingWalksTheStore()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: Columns(), PageSize: 2)));

        // [0] Name, [1] Qty, [2] prev, [3] p1, [4] p2, [5] p3, [6] next.
        var html = await grid.InvokeAsync(grid.HandlerIds("click")[5]); // page 3

        Assert.Equal(["Elderberry"], BodyCells(html, 0));
        Assert.Contains("5-5 / 5", html);
    }

    [Fact]
    public void Query_SortableColumnWithoutSortBy_IsNotSortable()
    {
        // The store orders by SortBy; without one the grid cannot honour a click, so it must not offer one.
        BsColumn<Row>[] columns =
        [
            new BsColumn<Row> { Title = "Name", Value = r => r.Name, Sortable = true },
            new BsColumn<Row> { Title = "Qty", Value = r => r.Qty, Sortable = true, SortBy = r => r.Qty },
        ];

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: All.AsQueryable(), Columns: columns, PageSize: 2)));

        Assert.Contains("<th scope=\"col\">Name</th>", grid.Html);
        Assert.Single(Regex.Matches(grid.Html, "aria-sort"));
    }

    [Fact]
    public void Query_EmptyResult_RendersTheEmptyState()
    {
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: new List<Row>().AsQueryable(), Columns: Columns(), PageSize: 2, Empty: Div["nothing"])));

        Assert.Contains("<div>nothing</div>", grid.Html);
        Assert.DoesNotContain("<table", grid.Html);
    }

    [Fact]
    public void Query_IsCached_SoAnUnrelatedRerenderDoesNotRequery()
    {
        // Render runs on every re-render. Without the cache, expanding a detail row would re-issue the query.
        var source = new CountingSource();
        var query = source.Rows().AsQueryable();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: query, Columns: Columns(), PageSize: 2)));

        var afterFirst = source.Executions;
        Assert.True(afterFirst > 0, "the first render must run the query");

        grid.Render();
        grid.Render();

        Assert.Equal(afterFirst, source.Executions);
    }

    [Fact]
    public async Task Query_ReRunsWhenTheSortOrPageChanges()
    {
        var source = new CountingSource();
        var query = source.Rows().AsQueryable();
        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: query, Columns: Columns(), PageSize: 2)));
        var afterFirst = source.Executions;

        await grid.InvokeAsync(grid.HandlerIds("click")[0]); // sort

        Assert.True(source.Executions > afterFirst, "a new sort must re-run the query");
    }

    [Fact]
    public void LazyEnumerableData_IsMaterialisedOnce_NotReEnumeratedPerRead()
    {
        // The grid reads the rows several times per render — count, sort, footers. A lazy sequence must be
        // materialised once, or the caller's LINQ chain silently re-runs on each pass.
        var source = new CountingSource();
        var lazy = source.Rows().Where(r => r.Qty > 0); // lazy, and NOT an IReadOnlyList

        var grid = RaskTest.Render(new Host(() => BsDataGrid<Row>(
            Data: lazy, Columns: Columns(), PageSize: 2)));

        Assert.Equal(1, source.Executions);
        Assert.Contains("1-2 / 5", grid.Html);
    }

    [Theory]
    [InlineData(RenderEngine.Server)]
    [InlineData(RenderEngine.InProcess)]
    public void Query_IsAllowed_WhereADatabaseCanExist(RenderEngine engine) =>
        // A native host's in-process engine can hold a local SQLite DbContext, so Query is legitimate there —
        // the guard is about WASM specifically, not about "not Server".
        BsDataGrid<Row>.EnsureQueryHost(engine);

    [Fact]
    public void Query_InWasm_ThrowsWithAnActionableMessage()
    {
        // A browser has no database. Fail with the fix in the message rather than as a null DbContext or a
        // trimmed-away LINQ provider somewhere far from the cause.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BsDataGrid<Row>.EnsureQueryHost(RenderEngine.Wasm));

        Assert.Contains("WASM", ex.Message);
        Assert.Contains("TotalCount", ex.Message);
    }
}
