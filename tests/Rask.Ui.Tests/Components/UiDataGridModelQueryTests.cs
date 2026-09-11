using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rask.Data;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The calling site of <c>Product.AsQueryable()</c>: a data grid handed a Rask.Data model query,
///     rendered against a real SQLite file.
/// </summary>
/// <remarks>
///     <para>
///         The grid's query path is synchronous — <c>Count()</c>, a boxed <c>OrderBy</c>, <c>Skip</c> and
///         <c>Take</c>, <c>ToList()</c> — and it runs again on every render. So what is worth pinning here
///         is that EF Core translates the grid's own LINQ, and that a click, which re-renders the same grid
///         over the same queryable, reaches the database again rather than a context the first render
///         left behind.
///     </para>
///     <para>
///         <c>Db</c> is process-wide static state. No other class in this assembly configures it, which is
///         what lets this one run in parallel with the rest; a second class that does would need an xUnit
///         collection shared with this one.
///     </para>
/// </remarks>
public sealed partial class UiDataGridModelQueryTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-ui-grid-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<GizmoContext> _options;

    public UiDataGridModelQueryTests()
    {
        _options = new DbContextOptionsBuilder<GizmoContext>().UseSqlite($"Data Source={_dbPath}").Options;

        using (var db = new GizmoContext(_options))
        {
            db.Database.EnsureCreated();
            db.AddRange(
                Gizmo.Create("Anvil", 30),
                Gizmo.Create("Bolt", 2),
                Gizmo.Create("Cog", 5),
                Gizmo.Create("Drill", 8));
            db.SaveChanges();
        }

        Db.Configure(() => new GizmoContext(_options));
    }

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public void A_sorted_page_of_a_model_query_is_read_from_the_database()
    {
        var html = UiDataGrid.Data(Gizmo.AsQueryable()).RowKey(g => g.Id).PageSize(2).Sort("stock")[c => [
            c.Field(g => g.Name).Title("Gizmo"),
            c.Field(g => g.Stock).Title("Stock").Sortable(true),
        ]].ToHtml();

        // The two lowest in stock, in order, and nothing else: SQLite ordered and sliced them.
        Assert.Equal(["Bolt", "Cog"], Names(html));
        Assert.Contains("4 rows", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sorting_and_paging_by_click_query_the_database_again()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Gizmo.AsQueryable()).RowKey(g => g.Id).PageSize(2)[c => [
            c.Field(g => g.Name).Title("Gizmo").Sortable(true),
            c.Field(g => g.Stock).Title("Stock"),
        ]]);

        await page.On("thead button:has-text(\"Gizmo\")").ClickAsync();
        Assert.Equal(["Anvil", "Bolt"], Names(page.Html));

        // Written between two renders of the same grid. The next render has to see it — a queryable
        // bound to the first render's context would still be counting four rows.
        await using (var db = new GizmoContext(_options))
        {
            db.Add(Gizmo.Create("Axle", 12));
            await db.SaveChangesAsync();
        }

        await page.On(".join button:has-text(\"2\")").ClickAsync();
        Assert.Equal(["Bolt", "Cog"], Names(page.Html));
        Assert.Contains("5 rows", page.Html, StringComparison.Ordinal);
    }

    // The first body cell of each row, in the order they are rendered.
    private static string[] Names(string html)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(r => Regex.Matches(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline))
            .Where(cells => cells.Count > 0)
            .Select(cells => cells[0].Groups[1].Value)
            .ToArray();
    }
}

internal sealed class Gizmo : Model<Guid>
{
    private Gizmo() { } // EF materialization

    public string Name { get; private set; } = "";

    public int Stock { get; private set; }

    public static Gizmo Create(string name, int stock) => new() { Id = Guid.NewGuid(), Name = name, Stock = stock };
}

// Hand-mapped rather than generated: this assembly does not run the model generator, and one entity
// needs one line.
internal sealed class GizmoContext(DbContextOptions<GizmoContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gizmo>();
        modelBuilder.ApplyRaskConventions();
    }
}
