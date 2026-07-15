using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.EfCore.Features.Catalog.Shared;

namespace Rask.Example.EfCore.Features.Catalog.ListProducts;

// The same catalogue as ListProductsPage, but paged and sorted by SQLite instead of by C#. Both grids below
// pass Data — the difference is what they pass: the query itself, or one already-fetched page. They suit
// different situations; see the notes on each.
[Route("products/grid")]
public sealed class ProductGridPage(IDbContextFactory<CatalogDbContext> dbContextFactory) : Component, IDisposable
{
    // --- The IQueryable way --------------------------------------------------------------------------------
    // Handing the grid an IQueryable re-runs the query during render, on every sort/page click, so the
    // DbContext behind it has to still be alive then. That rules out the `await using var db = ...` pattern the
    // rest of this sample uses: a context disposed at the end of a load would throw on the next click. So this
    // page owns one for its lifetime and disposes it on unmount.
    //
    // That is the real cost: a DbContext pinned to the session, which is exactly what ListProductsPage's
    // comment avoids. It is fine here — one page, one user, short-lived, read-only — but if that trade doesn't
    // suit you, the second grid below is the same feature without it.
    private readonly CatalogDbContext _db = dbContextFactory.CreateDbContext();

    // --- The async way -------------------------------------------------------------------------------------
    // Nothing is pinned: each load creates a context, awaits the query, and disposes it. The grid renders what
    // it is handed and reports clicks; this page runs the query in the awaited handler.
    private IReadOnlyList<Product> _rows = [];
    private int _total;
    private int _page;
    private string? _sort;
    private bool _desc;

    private const int Size = 5;

    public void Dispose() => _db.Dispose();

    protected override Component? Head => Title()["Product grid — Rask EF Core"];

    protected override async Task OnMountAsync() => await LoadAsync();

    // Runs on the click the grid awaits, so the rows are in place before the re-render paints. Count and page
    // are two round-trips — both awaited, neither blocking a thread.
    private async Task LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);

        var query = db.Products.AsNoTracking();
        query = _sort switch
        {
            "name" => _desc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "stock" => _desc ? query.OrderByDescending(p => p.Stock) : query.OrderBy(p => p.Stock),
            // A stable order matters: paging an unordered query is undefined.
            _ => query.OrderBy(p => p.Id),
        };

        _total = await query.CountAsync(CancellationToken);
        _rows = await query.Skip(_page * Size).Take(Size).ToListAsync(CancellationToken);
    }

    private async Task SortAsync(DataGridSort sort)
    {
        (_sort, _desc, _page) = (sort.Field, sort.Descending, 0); // a new order starts at the first page
        await LoadAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _page = page;
        await LoadAsync();
    }

    protected override Component? Render() =>
    [
        Div(Class: "mb-3")[
            H1(Class: "h3 mb-1")["Product grid"],
            P(Class: "text-secondary mb-0")[
                "Two ways to let SQLite do the paging and sorting. Both issue ORDER BY / COUNT / LIMIT — "
                + "neither ever loads the whole table."
            ]
        ],

        H2(Class: "h5")["An IQueryable as Data"],
        P(Class: "text-secondary small")[
            "Hand the grid the query itself. It orders by each column's SortBy expression, counts, and "
            + "materialises only the current page. It runs inside the synchronous render, so it blocks a "
            + "request thread — fine for an admin screen — and needs a DbContext that outlives the render."
        ],
        BsDataGrid(
            Id: "query-grid",
            // Ordered by Id: Skip/Take over an unordered SQL query is undefined, and EF warns about it.
            // Sorting a column replaces this ordering; it is the default the grid falls back to.
            Data: _db.Products.AsNoTracking().OrderBy(p => p.Id),
            PageSize: Size,
            RowKey: p => p.Id,
            Columns:
            [
                new BsColumn<Product>
                {
                    Title = "Product", Sortable = true,
                    // An Expression, not a Func: this becomes ORDER BY. A Func would sort in memory.
                    SortBy = p => p.Name.Value, Value = p => p.Name.Value,
                },
                new BsColumn<Product>
                {
                    Title = "Stock", Class = Txt.End(), Sortable = true,
                    SortBy = p => p.Stock.Value, Value = p => p.Stock.Value,
                },
                // No SortBy, so this header stays plain: the grid won't offer a sort it cannot translate.
                new BsColumn<Product> { Title = "Price", Class = Txt.End(), Value = p => p.Price.ToString() },
            ]),

        H2(Class: "h5 mt-4")["A fetched page + TotalCount"],
        P(Class: "text-secondary small")[
            "The same result, fully async. This page awaits CountAsync/ToListAsync in the handler the grid "
            + "awaits, then hands over one page plus the real total. Nothing blocks, and each load uses a "
            + "short-lived DbContext."
        ],
        BsDataGrid(
            Id: "async-grid",
            Data: _rows,
            TotalCount: _total,
            PageSize: Size,
            RowKey: p => p.Id,
            Page: _page,
            OnPageChangeAsync: GoToPageAsync,
            Sort: _sort,
            SortDescending: _desc,
            OnSortChangeAsync: SortAsync,
            Empty: Div(Class: "text-secondary")["No products yet."],
            Columns:
            [
                // SortField names the column in OnSortChange; the handler maps it to an OrderBy.
                new BsColumn<Product>
                {
                    Title = "Product", Sortable = true, SortField = "name", Value = p => p.Name.Value,
                },
                new BsColumn<Product>
                {
                    Title = "Stock", Class = Txt.End(), Sortable = true, SortField = "stock",
                    Value = p => p.Stock.Value,
                },
                new BsColumn<Product> { Title = "Price", Class = Txt.End(), Value = p => p.Price.ToString() },
            ])
    ];
}
