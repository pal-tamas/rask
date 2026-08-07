using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.EfCore.Features.Catalog.Shared;
using Rask.Example.EfCore.Shared.Forms;

namespace Rask.Example.EfCore.Features.Catalog.CreateProduct;

// Vertical slice: add a product (a command). The slice owns its own form model and form markup;
// the only things it shares are the domain (Product / value objects) and the FieldErrors template.
// Inline field validators reuse the value objects' Validate methods, so the form and the domain
// enforce the same rules from a single source.
[Route("products/new")]
public sealed partial class CreateProductPage(IDbContextFactory<CatalogDbContext> dbContextFactory, Navigator navigator)
    : Component
{
    private readonly CreateProductForm _form = new();

    protected override Component? Head => Title()["New product — Rask EF Core"];

    private async Task SubmitAsync(CreateProductForm form)
    {
        // The form already passed the same rules the domain enforces; Create is the final guard.
        var product = Product.Create(form.Name, form.Price, form.Stock);

        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken);

        navigator.NavigateTo("/products");
    }

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0 mx-auto", Style: "max-width: 32rem")[
            Div(Class: "card-body")[
                H1(Class: "h4 mb-3")["New product"],
                Form(_form, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
                    Div()[
                        Label("p-name", Class: "form-label small mb-1")["Name"],
                        Input(() => _form.Name, Validate: ProductName.Validate,
                            Id: "p-name", Class: "form-control"),
                        ValidationMessage(() => _form.Name, FieldErrors.Template)
                    ],
                    Div()[
                        Label("p-price", Class: "form-label small mb-1")["Price"],
                        Input(() => _form.Price, Validate: Money.Validate,
                            Id: "p-price", Class: "form-control", Step: "0.01"),
                        ValidationMessage(() => _form.Price, FieldErrors.Template)
                    ],
                    Div()[
                        Label("p-stock", Class: "form-label small mb-1")["Stock"],
                        Input(() => _form.Stock, Validate: StockLevel.Validate,
                            Id: "p-stock", Class: "form-control"),
                        ValidationMessage(() => _form.Stock, FieldErrors.Template)
                    ],
                    Div(Class: "d-flex justify-content-end gap-2 pt-2")[
                        NavLink("/products", Class: "btn btn-outline-secondary")["Cancel"],
                        Button("submit", Class: "btn btn-primary")[
                            I(Class: "bi bi-check2-circle me-1"), "Add product"
                        ]
                    ]
                ]
            ]
        ];
}

// The slice's own input model: mutable primitives the inputs bind to. It maps onto the aggregate's
// value objects at submit time.
public sealed class CreateProductForm
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; } = 1m;
    public int Stock { get; set; }
}
