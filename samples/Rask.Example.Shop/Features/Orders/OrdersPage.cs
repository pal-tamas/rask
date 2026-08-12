using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

public sealed record ListOrdersQuery : IQuery<IReadOnlyList<Order>>;

public sealed class ListOrdersQueryHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : IQueryHandler<ListOrdersQuery, IReadOnlyList<Order>>
{
    public async Task<IReadOnlyList<Order>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
    }
}

[Route("/orders")]
public sealed partial class OrdersPage(IDispatcher dispatcher) : Component
{
    private IReadOnlyList<Order> _items = [];
    private bool _loaded;

    protected override Component? HeadAssets => Title["Orders"];

    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _items = await dispatcher.DispatchAsync(new ListOrdersQuery(), CancellationToken);
        _loaded = true;
    }

    protected override Component? Render() =>
    [
        Div[
            H1["Orders"],

            NavLink.Href(Routes.CreateOrder())["New Order"]
        ],
        !_loaded
            ? Div["Loading…"]
            : _items.Count == 0
                ? Div["No Orders yet."]
                : Table[
                    Thead[
                        Tr[
                            Th["#"],
                            Th["Customer"],
                            Th["Total"],
                            Th[""]
                        ]
                    ],
                    Tbody[
                        _items.Select(x => Tr.Key(x.Id)[
                            Td[$"{x.Id}"],
                            Td[x.Customer.Value],
                            Td[$"{x.Total}"],
                            Td[
                                NavLink.Href(Routes.UpdateOrder(x.Id))["Edit"],
                                DeleteOrder.Id(x.Id).OnDeleted(LoadAsync)
                            ]
                        ])
                    ]
                ]
    ];
}
