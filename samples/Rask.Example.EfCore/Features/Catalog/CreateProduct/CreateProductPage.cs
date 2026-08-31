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

    protected override Component? HeadAssets => Title["New product — Rask EF Core"];

    private async Task SubmitAsync(CreateProductForm form)
    {
        // The form already passed the same rules the domain enforces; Create is the final guard.
        var product = Product.Create(form.Name, form.Price, form.Stock);

        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken);

        navigator.NavigateTo(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage());
    }

    protected override Component? Render() =>
        Div.Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("p-5")[
                H1.Class("text-xl font-semibold mb-3")["New product"],
                Form.Model(_form).OnValidSubmitAsync(SubmitAsync).Class("flex flex-col gap-3")[
                    Div[
                        Label.For("p-name").Class("mb-1 block text-sm font-medium")["Name"],
                        Input.Bind(() => _form.Name)
                            .Validate(ProductName.Validate)
                            .Id("p-name")
                            .Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Name)
                    ],
                    Div[
                        Label.For("p-price").Class("mb-1 block text-sm font-medium")["Price"],
                        Input.Bind(() => _form.Price)
                            .Validate(Money.Validate)
                            .Id("p-price")
                            .Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")
                            .Step("0.01"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Price)
                    ],
                    Div[
                        Label.For("p-stock").Class("mb-1 block text-sm font-medium")["Stock"],
                        Input.Bind(() => _form.Stock)
                            .Validate(StockLevel.Validate)
                            .Id("p-stock")
                            .Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100"),
                        ValidationMessage.Template(FieldErrors.Template).For(() => _form.Stock)
                    ],
                    Div.Class("flex justify-end gap-2 pt-2")[
                        NavLink.Href(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage()).Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-slate-700 ring-slate-300 hover:bg-slate-50 dark:text-slate-300 dark:ring-slate-600 dark:hover:bg-slate-800")["Cancel"],
                        Button.Type("submit").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500")[
                            Span.Class("me-1").Attributes(("aria-hidden", "true"))["✅"], "Add product"
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
