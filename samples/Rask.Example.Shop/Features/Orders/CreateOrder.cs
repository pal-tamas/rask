using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

public sealed record CreateOrderCommand(OrderRequest Request) : ICommand<Guid>;

public sealed class CreateOrderCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var entity = Order.Create(command.Request.Customer, command.Request.Total);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Orders.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public sealed partial class CreateOrder(IDispatcher dispatcher, Navigator navigator) : Page
{
    protected override string Route => "/orders/new";

    private readonly OrderRequest _form = new();
    private string? _error;

    protected override Component? HeadAssets => Title["New Order"];

    private async Task SubmitAsync(OrderRequest form)
    {
        try
        {
            await dispatcher.DispatchAsync(new CreateOrderCommand(form), CancellationToken);
            navigator.NavigateTo(Routes.OrdersPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }

    protected override Component? Render() =>
        Div[
            Div[
                H1["New Order"],
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
                        Button.Type("submit")["Save"]
                    ]
                ]
            ]
        ];
}
