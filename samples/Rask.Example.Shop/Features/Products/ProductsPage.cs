using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

public sealed record ListProductsQuery(bool IncludeDeleted = false) : IQuery<IReadOnlyList<Product>>;

public sealed class ListProductsQueryHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : IQueryHandler<ListProductsQuery, IReadOnlyList<Product>>
{
    public async Task<IReadOnlyList<Product>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var items = db.Products.AsNoTracking();
        if (query.IncludeDeleted)
        {
            items = items.IgnoreQueryFilters();
        }

        return await items.OrderBy(x => x.Id).ToListAsync(cancellationToken);
    }
}

[Route("/products")]
public sealed partial class ProductsPage(IDispatcher dispatcher) : Component
{
    private IReadOnlyList<Product> _items = [];
    private bool _loaded;
    private bool _showDeleted;

    protected override Component? HeadAssets => Title["Products"];

    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _items = await dispatcher.DispatchAsync(new ListProductsQuery(_showDeleted), CancellationToken);
        _loaded = true;
    }
    private async Task ToggleDeletedAsync()
    {
        _showDeleted = !_showDeleted;
        await LoadAsync();
    }


    protected override Component? Render() =>
    [
        Div[
            H1["Products"],
            Button.Type("button").OnClickAsync(ToggleDeletedAsync)[_showDeleted ? "Hide deleted" : "Show deleted"],
            NavLink.Href(Routes.CreateProduct())["New Product"]
        ],
        !_loaded
            ? Div["Loading…"]
            : _items.Count == 0
                ? Div["No Products yet."]
                : Table[
                    Thead[
                        Tr[
                            Th["#"],
                            Th["Name"],
                            Th["Price"],
                            Th["InStock"],
                            Th[""]
                        ]
                    ],
                    Tbody[
                        _items.Select(x => Tr.Key(x.Id)[
                            Td[$"{x.Id}"],
                            Td[x.Name.Value],
                            Td[$"{x.Price}"],
                            Td[$"{x.InStock}"],
                            Td[
                                x.DeletedAt is null ? NavLink.Href(Routes.UpdateProduct(x.Id))["Edit"] : null,
                                x.DeletedAt is null ? (Component)DeleteProduct.Id(x.Id).OnDeleted(LoadAsync) : RestoreProduct.Id(x.Id).OnRestored(LoadAsync)
                            ]
                        ])
                    ]
                ]
    ];
}
