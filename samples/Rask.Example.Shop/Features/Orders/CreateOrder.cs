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

[Route("/orders/new")]
public sealed partial class CreateOrder(IDispatcher dispatcher, Navigator navigator) : Component
{
    private readonly OrderRequest _form = new();
    private string? _error;

    protected override Component? Head => Title()["New Order"];

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
        Div()[
            Div()[
                H1()["New Order"],
                _error is null ? null : Div(Role: "alert")[_error],
                Form(_form, OnValidSubmitAsync: SubmitAsync)[
                    Div()[
                        Label("customer")["Customer"],
                        Input(() => _form.Customer).Validate(OrderCustomer.Validate).Id("customer")
                    ],
                    Div()[
                        Label("total")["Total"],
                        Input(() => _form.Total).Id("total")
                    ],
                    Div()[
                        NavLink(Routes.OrdersPage())["Cancel"],
                        Button("submit")["Save"]
                    ]
                ]
            ]
        ];
}
