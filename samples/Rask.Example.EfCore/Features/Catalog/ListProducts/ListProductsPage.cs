using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.EfCore.Features.Catalog.Shared;

namespace Rask.Example.EfCore.Features.Catalog.ListProducts;

// Vertical slice: list the catalogue (a query) and delete a row (a command). It talks to EF Core
// directly through the context factory — no repository abstraction — which is the vertical-slice
// stance: each slice owns its own data access.
[Route("/products")]
public sealed partial class ListProductsPage(IDbContextFactory<CatalogDbContext> dbContextFactory) : Component
{
    // One page, one canonical URL — that is what makes ListProductsPage.Url() unambiguous. This sample
    // also answers "/" so the app root lands on the catalogue; that second template is registered
    // explicitly in Program.cs, which is the escape hatch for the rare page that needs more than one.

    private IReadOnlyList<Product> _products = [];
    private bool _loaded;

    protected override Component? HeadAssets => Title["Products — Rask EF Core"];

    // Runs on every mount — navigating back from the create/edit slices remounts this page, so the
    // list always reflects the latest committed state.
    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        _products = await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(CancellationToken);
        _loaded = true;
    }

    // No explicit StateHasChanged() needed: an awaited event handler re-renders on completion, so
    // the reloaded list paints automatically (same as the async OnMountAsync above).
    private async Task DeleteAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(CancellationToken);
        await LoadAsync();
    }

    protected override Component? Render() =>
    [
        Div.Class("flex justify-between items-center mb-3")[
            Div[
                H1.Class("text-2xl font-semibold mb-1")["Products"],
                P.Class("text-slate-500 dark:text-slate-400 mb-0")["EF Core + SQLite CRUD, organised as vertical slices."]
            ],
            NavLink
                .Href(global::Rask.Example.EfCore.Features.Catalog.CreateProduct.Routes.CreateProductPage())
                .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500")[
                Span.Class("me-1").Attributes(("aria-hidden", "true"))["+"], "New product"
            ]
        ],
        !_loaded
            ? Div.Class("text-slate-500 dark:text-slate-400")[
                Span.Class("inline-block size-5 animate-spin rounded-full border-2 border-current border-r-transparent size-4 me-2"), "Loading…"
            ]
            : _products.Count == 0
                ? Div.Class("rounded-lg px-4 py-3 text-sm bg-sky-50 text-sky-900 dark:bg-sky-950 dark:text-sky-200")["No products yet — click \"New product\" to add one."]
                : Table.Class("w-full text-left text-sm [&_td]:px-3 [&_td]:py-2 [&_th]:px-3 [&_th]:py-2 [&_tbody_tr:nth-child(odd)]:bg-slate-50 align-middle bg-white shadow-sm rounded overflow-hidden")[
                    Thead[
                        Tr[
                            Th["#"],
                            Th["Name"],
                            Th.Class("text-right")["Price"],
                            Th.Class("text-right")["Stock"],
                            Th[""]
                        ]
                    ],
                    Tbody[
                        _products.Select(p => Tr.Key(p.Id)[
                            Td.Class("text-slate-500 dark:text-slate-400")[p.Id.ToString(CultureInfo.InvariantCulture)],
                            Td.Class("font-semibold")[p.Name.Value],
                            Td.Class("text-right")[p.Price.ToString()],
                            Td.Class("text-right")[p.Stock.Value.ToString(CultureInfo.InvariantCulture)],
                            Td.Class("text-right whitespace-nowrap")[
                                // Icon-only: without a name a screen reader announces "link" and
                                // nothing else, and the browser journey has nothing to address it by.
                                NavLink
                                    .Href($"/products/{p.Id}/edit")
                                    .Aria(new Dictionary<string, string?> { ["label"] = $"Edit {p.Name}" })
                                    .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-slate-700 ring-slate-300 hover:bg-slate-50 dark:text-slate-300 dark:ring-slate-600 dark:hover:bg-slate-800 me-1")[
                                    Span.Attributes(("aria-hidden", "true"))["✎"]
                                ],
                                Button
                                    .Type("button")
                                    .Aria(new Dictionary<string, string?> { ["label"] = $"Delete {p.Name}" })
                                    .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-red-700 ring-red-300 hover:bg-red-50 dark:text-red-300")
                                    .OnClickAsync(() => DeleteAsync(p.Id))[
                                    Span.Attributes(("aria-hidden", "true"))["🗑"]
                                ]
                            ]
                        ])
                    ]
                ]
    ];
}
