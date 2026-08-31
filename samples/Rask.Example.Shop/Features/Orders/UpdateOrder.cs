using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

public sealed record GetOrderQuery(Guid Id) : IQuery<Order?>;

public sealed class GetOrderQueryHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : IQueryHandler<GetOrderQuery, Order?>
{
    public async Task<Order?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
    }
}

public sealed record UpdateOrderCommand(Guid Id, OrderRequest Request) : ICommand;

public sealed class UpdateOrderCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<UpdateOrderCommand>
{
    public async Task HandleAsync(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Update(command.Request.Customer, command.Request.Total);
        await db.SaveChangesAsync(cancellationToken);
    }
}

[Route("/orders/{id:guid}/edit")]
public sealed partial class UpdateOrder(IDispatcher dispatcher, Navigator navigator) : Component
{
    private readonly OrderRequest _form = new();
    private bool _loaded;
    private bool _found;
    private string? _error;

    [RouteParam] public Guid Id { get; set; }

    protected override Component? HeadAssets => Title["Edit Order"];

    protected override async Task OnPropsChangedAsync()
    {
        _loaded = false;
        var entity = await dispatcher.QueryAsync(new GetOrderQuery(Id), CancellationToken);
        _found = entity is not null;
        if (entity is not null)
        {
            _form.Customer = entity.Customer.Value;
            _form.Total = entity.Total;
        }

        _loaded = true;
    }

    private async Task SubmitAsync(OrderRequest form)
    {
        try
        {
            await dispatcher.SendAsync(new UpdateOrderCommand(Id, form), CancellationToken);
            navigator.NavigateTo(Routes.OrdersPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }

    protected override Component? Render()
    {
        if (!_loaded)
        {
            return Div["Loading…"];
        }

        if (!_found)
        {
            return Div["Order not found. ", NavLink.Href(Routes.OrdersPage())["Back to the list"], "."];
        }

        return Div[
            Div[
                H1["Edit Order"],
                _error is null ? null : Div.Role("alert")[_error],
                Form.Model(_form).OnValidSubmitAsync(SubmitAsync)[
                    Div[
                        Label.For("customer")["Customer"],
                        Input.Bind(() => _form.Customer).Validate(OrderCustomer.Validate).Id("customer")
                    ],
                    Div[
                        Label.For("total")["Total"],
                        Input.Bind(() => _form.Total).Id("total")
                    ],
                    Div[
                        NavLink.Href(Routes.OrdersPage())["Cancel"],
                        Button.Type("submit")["Save changes"]
                    ]
                ]
            ]
        ];
    }
}
