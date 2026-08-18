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
        Div.Class("d-flex justify-content-between align-items-center mb-3")[
            Div[
                H1.Class("h3 mb-1")["Products"],
                P.Class("text-secondary mb-0")["EF Core + SQLite CRUD, organised as vertical slices."]
            ],
            NavLink
                .Href(global::Rask.Example.EfCore.Features.Catalog.CreateProduct.Routes.CreateProductPage())
                .Class("btn btn-primary")[
                I.Class("bi bi-plus-lg me-1"), "New product"
            ]
        ],
        !_loaded
            ? Div.Class("text-secondary")[
                Span.Class("spinner-border spinner-border-sm me-2"), "Loading…"
            ]
            : _products.Count == 0
                ? Div.Class("alert alert-info")["No products yet — click \"New product\" to add one."]
                : Table.Class("table table-striped align-middle bg-white shadow-sm rounded overflow-hidden")[
                    Thead[
                        Tr[
                            Th["#"],
                            Th["Name"],
                            Th.Class("text-end")["Price"],
                            Th.Class("text-end")["Stock"],
                            Th[""]
                        ]
                    ],
                    Tbody[
                        _products.Select(p => Tr.Key(p.Id)[
                            Td.Class("text-muted")[p.Id.ToString(CultureInfo.InvariantCulture)],
                            Td.Class("fw-semibold")[p.Name.Value],
                            Td.Class("text-end")[p.Price.ToString()],
                            Td.Class("text-end")[p.Stock.Value.ToString(CultureInfo.InvariantCulture)],
                            Td.Class("text-end text-nowrap")[
                                NavLink
                                    .Href($"/products/{p.Id}/edit")
                                    .Class("btn btn-outline-secondary btn-sm me-1")[
                                    I.Class("bi bi-pencil")
                                ],
                                Button
                                    .Type("button")
                                    .Class("btn btn-outline-danger btn-sm")
                                    .OnClickAsync(() => DeleteAsync(p.Id))[
                                    I.Class("bi bi-trash")
                                ]
                            ]
                        ])
                    ]
                ]
    ];
}
