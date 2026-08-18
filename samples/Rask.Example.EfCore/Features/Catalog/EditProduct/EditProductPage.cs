using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.EfCore.Features.Catalog.Shared;
using Rask.Example.EfCore.Shared.Forms;

namespace Rask.Example.EfCore.Features.Catalog.EditProduct;

// Vertical slice: edit a product (a command). Loads the current values into its own form, then on
// submit loads the tracked aggregate and mutates it through Product.Update so the invariants and
// the same value-object validation rules apply.
[Route("products/{id:int}/edit")]
public sealed partial class EditProductPage(IDbContextFactory<CatalogDbContext> dbContextFactory, Navigator navigator)
    : Component
{
    private readonly EditProductForm _form = new();
    private bool _loaded;
    private bool _found;

    [RouteParam] public int Id { get; set; }

    protected override Component? HeadAssets => Title["Edit product — Rask EF Core"];

    // Fires on first render and whenever Id changes — load the row to edit into the form.
    protected override async Task OnPropsChangedAsync()
    {
        _loaded = false;
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id, CancellationToken);

        _found = product is not null;
        if (product is not null)
        {
            _form.Name = product.Name.Value;
            _form.Price = product.Price.Amount;
            _form.Stock = product.Stock.Value;
        }

        _loaded = true;
    }

    private async Task SubmitAsync(EditProductForm form)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == Id, CancellationToken);
        if (product is not null)
        {
            product.Update(form.Name, form.Price, form.Stock);
            await db.SaveChangesAsync(CancellationToken);
        }

        navigator.NavigateTo(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage());
    }

    protected override Component? Render()
    {
        if (!_loaded)
        {
            return Div.Class("text-secondary")[
                Span.Class("spinner-border spinner-border-sm me-2"), "Loading…"
            ];
        }

        if (!_found)
        {
            return Div.Class("alert alert-warning")[
                "Product not found. ", NavLink.Href(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage())["Back to the list"], "."
            ];
        }

        return Div.Class("card shadow-sm border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("card-body")[
                H1.Class("h4 mb-3")["Edit product"],
                Form.Model(_form).OnValidSubmitAsync(SubmitAsync).Class("vstack gap-3")[
                    Div[
                        Label.For("p-name").Class("form-label small mb-1")["Name"],
                        Input.Bind(() => _form.Name)
                            .Validate(ProductName.Validate)
                            .Id("p-name")
                            .Class("form-control"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Name)
                    ],
                    Div[
                        Label.For("p-price").Class("form-label small mb-1")["Price"],
                        Input.Bind(() => _form.Price)
                            .Validate(Money.Validate)
                            .Id("p-price")
                            .Class("form-control")
                            .Step("0.01"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Price)
                    ],
                    Div[
                        Label.For("p-stock").Class("form-label small mb-1")["Stock"],
                        Input.Bind(() => _form.Stock)
                            .Validate(StockLevel.Validate)
                            .Id("p-stock")
                            .Class("form-control"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Stock)
                    ],
                    Div.Class("d-flex justify-content-end gap-2 pt-2")[
                        NavLink.Href(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage()).Class("btn btn-outline-secondary")["Cancel"],
                        Button.Type("submit").Class("btn btn-primary")[
                            I.Class("bi bi-check2-circle me-1"), "Save changes"
                        ]
                    ]
                ]
            ]
        ];
    }
}

public sealed class EditProductForm
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; } = 1m;
    public int Stock { get; set; }
}
