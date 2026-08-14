using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

public sealed record CreateProductCommand(ProductRequest Request) : ICommand<Guid>;

public sealed class CreateProductCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var entity = Product.Create(command.Request.Name, command.Request.Price, command.Request.InStock);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Products.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

[Route("/products/new")]
public sealed partial class CreateProduct(IDispatcher dispatcher, Navigator navigator) : Component
{
    private readonly ProductRequest _form = new();
    private string? _error;

    protected override Component? HeadAssets => Title["New Product"];

    private async Task SubmitAsync(ProductRequest form)
    {
        try
        {
            await dispatcher.DispatchAsync(new CreateProductCommand(form), CancellationToken);
            navigator.NavigateTo(Routes.ProductsPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }

    protected override Component? Render() =>
        Div[
            Div[
                H1["New Product"],
                _error is null ? null : Div.Role("alert")[_error],
                Form.Model(_form).OnValidSubmitAsync(SubmitAsync)[
                    Input.Bind(() => _form.Version).Type(InputType.Hidden),
                    Div[
                        Label.For("name")["Name"],
                        Input.Bind(() => _form.Name).Validate(ProductName.Validate).Id("name")
                    ],
                    Div[
                        Label.For("price")["Price"],
                        Input.Bind(() => _form.Price).Id("price")
                    ],
                    Div[
                        Label.For("instock")["InStock"],
                        Input.Bind(() => _form.InStock).Id("instock")
                    ],
                    Div[
                        NavLink.Href(Routes.ProductsPage())["Cancel"],
                        Button.Type("submit")["Save"]
                    ]
                ]
            ]
        ];
}
