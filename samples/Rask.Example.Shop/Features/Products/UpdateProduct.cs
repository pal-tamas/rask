using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

public sealed record GetProductQuery(Guid Id) : IQuery<Product?>;

public sealed class GetProductQueryHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : IQueryHandler<GetProductQuery, Product?>
{
    public async Task<Product?> HandleAsync(GetProductQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
    }
}

public sealed record UpdateProductCommand(Guid Id, ProductRequest Request) : ICommand;

public sealed class UpdateProductCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<UpdateProductCommand>
{
    public async Task HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Products.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.Entry(entity).Property(x => x.Version).OriginalValue = command.Request.Version;
        entity.Update(command.Request.Name, command.Request.Price, command.Request.InStock);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed partial class UpdateProduct(IDispatcher dispatcher, Navigator navigator) : Page
{
    protected override string Route => "/products/{id:guid}/edit";

    private readonly ProductRequest _form = new();
    private bool _loaded;
    private bool _found;
    private string? _error;

    [RouteParam] public Guid Id { get; set; }

    protected override Component? HeadAssets => Title["Edit Product"];

    protected override async Task OnPropsChangedAsync()
    {
        _loaded = false;
        var entity = await dispatcher.DispatchAsync(new GetProductQuery(Id), CancellationToken);
        _found = entity is not null;
        if (entity is not null)
        {
            _form.Name = entity.Name.Value;
            _form.Price = entity.Price;
            _form.InStock = entity.InStock;
            _form.Version = entity.Version;
        }

        _loaded = true;
    }

    private async Task SubmitAsync(ProductRequest form)
    {
        try
        {
            await dispatcher.DispatchAsync(new UpdateProductCommand(Id, form), CancellationToken);
            navigator.NavigateTo(Routes.ProductsPage());
        }
        catch (DbUpdateConcurrencyException)
        {
            _error = "This record changed since you opened it — reload and reapply your edits.";
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
            return Div["Product not found. ", NavLink.Href(Routes.ProductsPage())["Back to the list"], "."];
        }

        return Div[
            Div[
                H1["Edit Product"],
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
                        Button.Type("submit")["Save changes"]
                    ]
                ]
            ]
        ];
    }
}
