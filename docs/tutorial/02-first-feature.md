# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll write:** a vertical slice under `Features/Products/`, then run `rask db add` / `rask db update`.

This chapter sets the pattern every later feature repeats — **entity → pages → migrate**. Do it once here
and the rest of the tutorial is variations on it.

Everything below is code you write. It's longer than the chapters that follow because it's the only one
that shows a slice end to end; once you've typed it, the shape is yours and later chapters only show
what's new.

> **You don't name a database.** Chapter 1's `rask new` already wired one — `AppDbContext` in
> `Features/Shared/`. Its base, `RaskDbContext`, maps every entity you declare, so you never edit it to add
> one, and an app keeps **one** database and one set of migrations however many features you add.

## 1. The entity

`Features/Products/Product.cs`. The constructor is private and the setters are `private set`, so nothing
outside can build a `Product` halfway or poke a field — the ways in are the ones Rask generates from it,
which you'll meet in a moment:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Shop.Features.Products;

public sealed class Product : Model<Guid>, ITimestamped, IVersioned
{
    private Product() { } // EF Core materialization

    [Required, MaxLength(200)]
    public string Name { get; private set; } = "";

    public decimal Price { get; private set; }

    public bool InStock { get; private set; }

    public int Version { get; private set; }
}
```

`Model<Guid>` comes from [Rask.Data](../data.md). It supplies the `Id`, and mapping the class needs
nothing else — no `DbSet` property, no configuration class, no registration. `[Required, MaxLength(200)]`
does two jobs at once: EF Core reads it as the column (`NOT NULL`, 200 characters), and the form reads it
as validation. The `private set`s aren't a style choice: the build rejects a public `set` or `init` on an
entity (RASK080), because an entity's state changes through its own methods and the generated writes, never
by assignment from outside.

The two markers are opt-in, and opting in costs nothing in the class:

- **`ITimestamped`** — `CreatedAt`/`UpdatedAt` columns exist and are filled in on every write, without
  appearing on the type. Declare `public DateTime CreatedAt { get; private set; }` only when a screen
  needs to show it.
- **`IVersioned`** — a concurrency token, bumped on every update. It is the one marker that must declare
  its property, because an edit form has to carry the version it was loaded at. It's what stops two people
  editing the same product from silently overwriting each other — you'll see it at work in section 4.

## 2. What Rask generates from it

Build once, and the generator gives `Product` a **form model** and the writes that take it, in the same
namespace. You never write or open this — it's here so you know what you're calling:

```csharp
// Generated for you. Don't add this to your project.
public sealed partial class ProductModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public decimal Price { get; set; }

    public bool InStock { get; set; }

    public int Version { get; set; }
}

// …and on Product itself:
//   Product.CreateAsync(ProductModel model, CancellationToken ct = default)                 → Task<Product>
//   Product.UpdateAsync(Guid id, ProductModel model, CancellationToken ct = default)        → Task<Product>
//   Product.DeleteAsync(Guid id, int? version = null, CancellationToken ct = default)       → Task
//   product.ToModel()                                                                       → ProductModel
```

`ProductModel` is the half of the entity a form is allowed to touch: mutable, so it can be half-filled
and invalid while someone types, while the `Product` it saves into never is. The validation attributes
are copied across, so a rule you declared once on the entity applies to the form too. The audit columns
and navigation properties are left out; mark a property `[SkipModel]` to keep it out of the form as well
— a stock count only a background job updates, say.

**The key is left out too.** A new row's id is generated when it's inserted, so `CreateAsync` never needs
one, and `UpdateAsync` takes the id of the row to change as its own argument — from the page's route, never
from the form. A form model has no `Id` to tamper with, so a posted form can't be pointed at somebody else's
row.

Each write opens a database context, makes one change, saves, and disposes the context before it returns
— and it saves through the same interceptors as any other save, so the timestamps and the version check
apply. Reads work the same way from the other side: `Product.Where(…)`, `Product.FindAsync(id)` and
`Product.AsQueryable()` each open their own context and hand back rows nothing is tracking. That's what
makes it safe to call them straight from a page: a Rask page lives as long as the browser keeps its socket
open, and nothing here holds a context between calls.

## 3. The create page

`Features/Products/CreateProduct.cs`:

```csharp
using Rask.Core.Routing;

namespace Shop.Features.Products;

[Route("/products/new")]
public sealed partial class CreateProduct(Navigator navigator) : Component
{
    private readonly ProductModel _model = new();
    private string? _error;

    protected override Component? HeadAssets => Title["New Product"];

    private async Task SaveAsync(ProductModel model)
    {
        try
        {
            await Product.CreateAsync(model, CancellationToken);
            navigator.NavigateTo(Routes.ProductsPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }

    protected override Component? Render() =>
    [
        UiHeader.Heading("New product").Actions(NavLink.Href(Routes.ProductsPage())["Cancel"]),
        UiCard[
            _error is null ? null : UiAlert.Tone(UiTone.Error)[_error],
            Form.Model(_model).OnValidSubmit(SaveAsync)[
                UiInput.Bind(() => _model.Name).Label("Name"),
                UiInput.Bind(() => _model.Price).Label("Price").Min("0").Step("0.01")
                    .Hint("What a customer pays, before tax."),
                UiCheckbox.Bind(() => _model.InStock).Text("In stock"),
                UiButton.Type(UiButtonType.Submit).Tone(UiTone.Primary)["Save"]
            ]
        ]
    ];
}
```

`Routes.ProductsPage()` is generated from the `[Route]` on the list page you're about to write — a typed
URL, so renaming a route breaks the build instead of the link. See [routing](../routing.md).

The page is built from the [Rask.Ui kit](../ui-kit.md), so there isn't a class string in it. `UiInput` is
a whole field in one line: its label floats inside the box until you type (put guidance in `Hint`, under
the field, rather than in a placeholder), and the field's own validation message appears under it.
`UiButton.Type(UiButtonType.Submit)` is the form's submit button. The one plain tag is `NavLink`, for
**Cancel**: a link inside your own app stays a `NavLink`, because that is what the runtime navigates without
reloading the page.

Nothing in that form mentions validation, and the `[Required]` / `[MaxLength]` you put on `Product` are
still enforced: they were copied onto `ProductModel`, and `Form<T>` validates its model on its own, with
no package to add and nothing to declare. `SaveAsync` only runs for a valid model. See
[forms](../forms.md) and [validation](../validation.md).

## 4. Edit and delete

Same shape, and the list page in the next step links to both — so write them now or it won't compile.

`Features/Products/UpdateProduct.cs` loads the row, turns it into a form model, and saves the model back:

```csharp
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;

namespace Shop.Features.Products;

[Route("/products/{id:guid}/edit")]
public sealed partial class UpdateProduct(Navigator navigator) : Component
{
    private ProductModel _model = new();
    private bool _loaded;
    private bool _found;
    private string? _error;

    [RouteParam] public Guid Id { get; set; }

    protected override Component? HeadAssets => Title["Edit Product"];

    protected override async Task OnPropsChangedAsync()
    {
        _loaded = false;
        var product = await Product.FindAsync(Id, CancellationToken);
        _found = product is not null;
        if (product is not null)
        {
            _model = product.ToModel();
        }

        _loaded = true;
    }

    private async Task SaveAsync(ProductModel model)
    {
        try
        {
            await Product.UpdateAsync(Id, model, CancellationToken);
            navigator.NavigateTo(Routes.ProductsPage());
        }
        catch (DbUpdateConcurrencyException)
        {
            _error = "Someone else changed this product while you were editing it. Reload to see their changes.";
        }
        catch (KeyNotFoundException)
        {
            _error = "This product has been deleted.";
        }
    }

    protected override Component? Render()
    {
        if (!_loaded)
        {
            return UiLoading.Text("Loading…");
        }

        if (!_found)
        {
            return UiAlert.Tone(UiTone.Warning)[
                "Product not found. ", NavLink.Href(Routes.ProductsPage())["Back to the list"], "."
            ];
        }

        return
        [
            UiHeader.Heading("Edit product").Actions(NavLink.Href(Routes.ProductsPage())["Cancel"]),
            UiCard[
                _error is null ? null : UiAlert.Tone(UiTone.Error)[_error],
                Form.Model(_model).OnValidSubmit(SaveAsync)[
                    UiInput.Bind(() => _model.Name).Label("Name"),
                    UiInput.Bind(() => _model.Price).Label("Price").Min("0").Step("0.01")
                        .Hint("What a customer pays, before tax."),
                    UiCheckbox.Bind(() => _model.InStock).Text("In stock"),
                    UiButton.Type(UiButtonType.Submit).Tone(UiTone.Primary)["Save changes"]
                ]
            ]
        ];
    }
}
```

Four things in that page are doing more than they look:

- **The id comes from the route.** `UpdateAsync(Id, model)` names the row by the page's `[RouteParam]`;
  the model only carries what the form edits.
- **`ToModel()` copies the `Version` too.** That is the whole of optimistic concurrency here: the model
  remembers the version the form was loaded at, and `UpdateAsync` refuses to write if the row has moved on
  since, throwing `DbUpdateConcurrencyException` rather than overwriting someone else's edit. It writes
  only the columns that actually changed, and a row deleted in the meantime is a `KeyNotFoundException`.
- **The row `FindAsync` returns is untracked.** Changing it and hoping it saves would do nothing — which is
  exactly why edits go through the model.
- **`OnPropsChangedAsync`, not a constructor or `OnInitialized`.** A Rask component loads its data from its
  lifecycle hooks: `OnMountAsync` once, `OnPropsChangedAsync` whenever its props — here the route's `Id` —
  change. The [lifecycle](../lifecycle.md) guide has the full order.

`Features/Products/DeleteProduct.cs` is a small reusable button the list page drops next to each row:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

// A reusable delete button: deletes the product, then invokes OnDeleted so the caller (the list page)
// can refresh.
public sealed partial class DeleteProduct : Component
{
    public Guid Id { get; set; }

    // The version the row was read at, so a delete can't remove a product someone has just edited.
    public int Version { get; set; }

    // Callback, not Func<Task>. A delegate-typed property is INVOCABLE, so `DeleteProduct.OnDeleted(x)`
    // at the call site binds to the property being called rather than to the generated chain setter,
    // and the compiler reports CS1593 on a delegate that "does not take 1 arguments". Callback is a
    // struct with no invocation, so lookup finds nothing applicable and falls through to the setter.
    public Callback? OnDeleted { get; set; }

    private async Task DeleteAsync()
    {
        try
        {
            await Product.DeleteAsync(Id, Version, CancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or KeyNotFoundException)
        {
            // Someone edited or deleted it first. Refreshing the list shows the reader what happened.
        }

        // Invoke() hands back the Task for an async handler and null for a synchronous one, which is
        // what keeps a sync handler off the async path.
        if (OnDeleted?.Invoke() is { } pending)
        {
            await pending;
        }
    }

    protected override Component? Render() =>
        UiButton.Tone(UiTone.Error).Variant(UiVariant.Ghost).Size(UiSize.Sm).OnClick(DeleteAsync)["Delete"];
}
```

## 5. The list page

`Features/Products/ProductsPage.cs` — the routed page, over a data grid:

```csharp
using Rask.Core.Routing;

namespace Shop.Features.Products;

[Route("/products")]
public sealed partial class ProductsPage : Component
{
    // A query, not a list. It holds no database connection: the grid runs it — sorted and paged in
    // SQL — each time it renders, and each run opens and disposes its own context.
    private readonly IQueryable<Product> _products = Product.OrderBy(p => p.Name).AsQueryable();

    protected override Component? HeadAssets => Title["Products"];

    protected override Component? Render() =>
    [
        UiHeader.Heading("Products").Actions(NavLink.Href(Routes.CreateProduct())["New product"]),
        UiDataGrid.Data(_products).RowKey(p => p.Id).PageSize(20).Label("Products")[c => [
            c.Field(p => p.Name).Title("Name").Sortable(true),
            c.Field(p => p.Price).Title("Price").Sortable(true),
            c.Field(p => p.InStock).Title("In stock"),
            c.Column().Title("Actions").Cell(p => Div[
                NavLink.Href(Routes.UpdateProduct(p.Id))["Edit"],
                // The grid re-runs its query on every render, so asking for one is the whole refresh.
                DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(StateHasChanged)
            ]),
        ]]
    ];
}
```

`Product.AsQueryable()` is a standard `IQueryable<Product>` that holds no context, which is the shape
[`UiDataGrid`](../data-grid.md) wants: clicking a sortable header becomes `ORDER BY`, and the pager becomes
`Skip`/`Take`, so the database does the work however large the catalog grows. `RowKey` is required — it
is what the grid identifies a row by when it redraws.

Note what the page doesn't have: an `OnMountAsync`. Nothing needs loading up front, because the grid runs
the query when it renders. When a page does need data before it draws — a count for a heading, say —
that's where it goes: `_count = await Product.CountAsync(CancellationToken);`.

## 6. Already registered

Chapter 1's `rask new` wrote everything this slice needs into `Program.cs`, so there's nothing to add. The
two parts it depends on are worth recognising:

```csharp
builder.Services.AddRaskData<AppDbContext>();
var connectionString = builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db";
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(connectionString, o => o.StrictTables = true)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

and one line after the container is built:

```csharp
var app = builder.Build();

Db.Configure(app.Services);
```

- `AddRaskData<AppDbContext>()` registers the interceptors (auditing, and later soft-delete, concurrency
  and events) **and names the context to the model surface**. The type argument is what makes
  `Product.Where(…)` and `Product.CreateAsync(…)` know which database to open.
- `Db.Configure(app.Services)` points the model surface at it, once, after the container exists. Without
  this pair the app builds and serves, and throws `The model database has not been configured` on the first
  line of data code.
- `AddDbContextFactory<AppDbContext>(…)` registers the context **as a factory**, not as a shared context.
  Rask pages are long-lived and can render concurrently, so every read and write makes its own short-lived
  context instead of sharing one. `UseRaskSqlite` is a drop-in for `UseSqlite` that also applies the
  production pragmas (WAL, `busy_timeout`, `foreign_keys`) — so the app handles concurrent writers (the
  jobs, email, and outbox you add in later chapters) without hitting `database is locked`. It defaults to a
  local `app.db` file next to the app but honours a `ConnectionStrings:App` override, which is how a deploy
  points it at a persistent volume.

When you need EF Core itself — a domain operation with its own rules, or several changes in one
transaction — you inject that same factory, `IDbContextFactory<AppDbContext>`, and call
`SaveChangesAsync`. [Chapter 7](07-outbox-events.md) does exactly that.

## 7. Create the table

The code is ready, but `app.db` has no table for `Product` yet. EF Core **migrations** generate the schema
from your entities. `rask new` already created and applied the first one — the batteries' tables — and
`rask db` wraps the EF tooling for every one after it:

```bash
rask db add AddProduct        # generate a migration for what changed in the model
rask db update                # apply it — adds the Product table to app.db
```

`rask db add` writes into the `Migrations/` folder you commit alongside your code; `rask db update` runs it
against `app.db`. Every time you change an entity later, it's the same pair: `rask db add <Name>` then
`rask db update`.

## 8. Run it

```bash
rask dev
```

Browse to **`/products`**. You get a sortable, paged list with **New**, **Edit**, and **Delete** — each one
reading or writing SQLite through the model. Create a product and refresh: it's still there, because it's
on disk in `app.db`.

## Verify

- `Features/Products/` holds the entity and four components — no request class, no configuration class,
  and `AppDbContext` is untouched.
- The app builds, and `ProductModel` resolves (your IDE shows it as generated).
- After `rask db update`, `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).
- Open the same product's edit page in two tabs and save both: the second shows "Someone else changed
  this product" instead of overwriting the first.

> **Troubleshooting.** `rask db` can't find the project → make sure you `cd`'d into `Shop` first.
> `/products` fails with `no such table` → you skipped `rask db update`. The build can't find
> `ProductModel`, `Product.CreateAsync` or `Routes.ProductsPage()` → those are generated; build once and the
> IDE catches up. For a route, the generator also needs the `[Route]` attribute on the page.

**Learn more:** [data access](../data-access.md) · [Rask.Data](../data.md) · [forms](../forms.md) ·
[data grid](../data-grid.md) · [the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
