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
            return Div.Class("text-slate-500 dark:text-slate-400")[
                Span.Class("inline-block size-5 animate-spin rounded-full border-2 border-current border-r-transparent size-4 me-2"), "Loading…"
            ];
        }

        if (!_found)
        {
            return Div.Class("rounded-lg px-4 py-3 text-sm bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200")[
                "Product not found. ", NavLink.Href(global::Rask.Example.EfCore.Features.Catalog.ListProducts.Routes.ListProductsPage())["Back to the list"], "."
            ];
        }

        return Div.Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("p-5")[
                H1.Class("text-xl font-semibold mb-3")["Edit product"],
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
                            Span.Class("me-1").Attributes(("aria-hidden", "true"))["✅"], "Save changes"
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
