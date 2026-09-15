# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll write:** a vertical slice under `Features/Products/` — an aggregate and its pages — then run
> `rask db add` / `rask db update`.

This chapter sets the pattern every later feature repeats — **aggregate → pages → migrate**. Do it once here
and the rest of the tutorial is variations on it.

Everything below is code you write. It's longer than the chapters that follow because it's the only one
that shows a slice end to end; once you've typed it, the shape is yours and later chapters only show
what's new.

> **You don't name a database.** Chapter 1's `rask new` already wired one — `AppDbContext` in
> `Features/Shared/`. Its base, `RaskDbContext`, maps every aggregate you declare, so you never edit it to add
> one, and an app keeps **one** database and one set of migrations however many features you add.

## 1. The aggregate

`Features/Products/Product.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Shop.Features.Products;

public sealed class Product : Aggregate<Guid>
{
    [Required, MaxLength(200)]
    public string Name { get; private set; } = "";

    [Range(0, 1_000_000)]
    public decimal Price { get; private set; }

    public bool InStock { get; private set; } = true;
}
```

`Aggregate<Guid>` comes from [Rask.Data](../data.md). An **aggregate** is the unit your app loads, changes and
saves as one: here, a product. Deriving from it is all the mapping there is (no `DbSet` property, no
configuration class, no registration) and it brings the columns every aggregate needs:

- **`Id`**, the key: a `Guid`, generated for you when a product is created.
- **`CreatedAt` / `UpdatedAt`**, stamped on every save.
- **`Version`**, a concurrency token bumped on every update. It's what stops two people editing the same
  product from silently overwriting each other, and you'll see it at work in section 4.
- **`DeletedAt`**, so a delete hides the row instead of destroying it.

They're ordinary read-only properties, so a page can show `product.CreatedAt` or sort by it.

Two rules shape the class, and the build enforces both:

- **Setters are `private set`.** A public setter on an aggregate is a build error
  ([RASK084](../diagnostics.md#rask084)): state changes only through the aggregate's own code, so nothing outside
  can poke a field. EF Core and the forms below both write through private setters.
- **No constructor.** The implicit one is what EF Core builds rows with and what a new form starts from. When an
  aggregate needs a domain way to be created, it gets a static factory; chapter 7 gives `Order` one.

`[Required, MaxLength(200)]` is read twice: EF Core makes the column `NOT NULL` and 200 characters, and the
form checks it as the user types. `[Range]` is only for the form. `InStock`'s `= true` is the default a new
product starts with, on the create form too.

**Reading needs nothing more.** `Product.Where(…)`, `Product.FindAsync(id)` and `Product.AsQueryable()` are
on the type already. Each opens its own database context, runs, and disposes it before it returns, and
hands back rows nothing is tracking. That's what makes it safe to call them straight from a page: a Rask
page lives as long as the browser keeps its socket open, and nothing here holds a context between calls.

## 2. The form model and the writes

A form edits something mutable, and `Product` isn't. So the build generates a **form model** beside it,
`ProductModel`, and every form binds that, never the aggregate:

- it has a settable copy of `Name`, `Price` and `InStock`, plus the `Version` an edit was loaded at, and never
  the `Id`;
- **every property is nullable**, because a form field can be empty whatever the column is;
- `[Required]`, `[MaxLength]` and `[Range]` are copied onto it, so the form validates by the aggregate's rules;
- **`new ProductModel()`** starts with the aggregate's defaults (`InStock` is `true`), and **`product.ToModel()`**
  fills one from a row for an edit form.

The writes are on the type, beside the reads, and take the model:

```csharp
var product = await Product.CreateAsync(model);   // a new row with a new Id
await Product.UpdateAsync(id, model);             // only the changed columns; a stale Version throws
await Product.DeleteAsync(id, version);           // stamps DeletedAt, and reads stop seeing it
```

Each one opens a context, saves through the interceptors `rask new` wired (so the timestamps and `Version` are
looked after), and disposes it. There's nothing to inject and nothing to write here: the next three sections are
just pages.

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
            await Product.CreateAsync(model, cancellationToken: CancellationToken);
            navigator.NavigateTo(Routes.ProductsPage());
        }
        catch (Exception)
        {
            _error = "Something went wrong — please try again.";
        }
    }

    protected override Component? Render() =>
    [
        UiHeader.Heading("New product").Actions(UiButton.Variant(UiVariant.Ghost).Href(Routes.ProductsPage())["Cancel"]),
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
`UiButton.Type(UiButtonType.Submit)` is the form's submit button, and **Cancel** is a `UiButton` too. Given
`Href` it renders as a link, and because `Routes.ProductsPage()` is a generated route rather than a string,
the runtime follows it without reloading the page. See [the UI kit](../ui-kit.md#buttons-and-links-that-go-somewhere).

Nothing in that form mentions validation, and the attributes on `Product` are still enforced: `Form<T>`
validates its model on its own, with no package to add and nothing to declare, and `SaveAsync` only runs for a
valid one. See [forms](../forms.md) and [validation](../validation.md).

## 4. Edit and delete

Same shape, and the list page in the next step links to both — so write them now or it won't compile.

`Features/Products/UpdateProduct.cs` loads the row, turns it into a `ProductModel`, and saves it back:

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
            await Product.UpdateAsync(Id, model, cancellationToken: CancellationToken);
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
                "Product not found. ", UiLink.Href(Routes.ProductsPage()).Text("Back to the list"), "."
            ];
        }

        return
        [
            UiHeader.Heading("Edit product").Actions(UiButton.Variant(UiVariant.Ghost).Href(Routes.ProductsPage())["Cancel"]),
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

- **The row comes from the route.** The page looks the product up by its `[RouteParam]` and saves to that same
  `Id`. The model carries no id, so no input on the form can point the save at another row.
- **`ToModel()` copies the `Version` too.** That is the whole of optimistic concurrency here: the model
  remembers the version the form was loaded at, and `UpdateAsync` refuses to write if the row has moved on
  since, throwing `DbUpdateConcurrencyException` rather than overwriting someone else's edit. A row deleted
  in the meantime is a `KeyNotFoundException`.
- **The save writes what the form holds.** `UpdateAsync` loads the row, copies the model onto it and saves only
  the columns that changed. The row `FindAsync` returned is untracked, so changing it directly would save
  nothing; the model is how a change gets back.
- **`OnPropsChangedAsync`, not a constructor or `OnInitialized`.** A Rask component loads its data from its
  lifecycle hooks: `OnMountAsync` once, `OnPropsChangedAsync` whenever its props — here the route's `Id` —
  change. The [lifecycle](../lifecycle.md) guide has the full order.

`Features/Products/DeleteProduct.cs` is a small reusable button the list page drops next to each row:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

// A reusable delete button: removes the product, then invokes OnDeleted so the caller (the list page)
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
            await Product.DeleteAsync(Id, Version, cancellationToken: CancellationToken);
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

A delete is a **soft delete**: `DeletedAt` is stamped and every read, the list's included, stops seeing the row,
while the data stays in `app.db`.

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
        UiHeader.Heading("Products").Actions(UiButton.Tone(UiTone.Primary).Href(Routes.CreateProduct())["New product"]),
        UiDataGrid.Data(_products).RowKey(p => p.Id).PageSize(20).Label("Products")[c => [
            c.Field(p => p.Name).Title("Name").Sortable(true),
            c.Field(p => p.Price).Title("Price").Sortable(true),
            c.Field(p => p.InStock).Title("In stock"),
            c.Field(p => p.UpdatedAt).Title("Updated").Sortable(true),
            c.Column().Title("Actions").Cell(p => Div[
                UiButton.Variant(UiVariant.Ghost).Size(UiSize.Sm).Href(Routes.UpdateProduct(p.Id))["Edit"],
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
is what the grid identifies a row by when it redraws. The grid shows the aggregates themselves, read-only;
`UpdatedAt` is one of the columns `Aggregate<Guid>` brought, sortable like any other.

Note what the page doesn't have: an `OnMountAsync`. Nothing needs loading up front, because the grid runs
the query when it renders. When a page does need data before it draws — a count for a heading, say —
that's where it goes: `_count = await Product.CountAsync(CancellationToken);`.

## 6. Already registered

Chapter 1's `rask new` wrote everything this slice needs into `Program.cs`, so there's nothing to add. The
two parts it depends on are worth recognising:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

…with the connection string it reads, in `appsettings.json` (a `--no-data` scaffold has none yet):

```jsonc
"Rask": {
  "ConnectionStrings": {
    "App": "Data Source=app.db"
  }
}
```

and one line after the container is built:

```csharp
var app = builder.Build();

Db.Configure(app.Services);
```

- `AddRaskData<AppDbContext>()` registers the interceptors (timestamps, versions, soft delete and events)
  **and names the context to the model surface**. The type argument is what makes `Product.Where(…)` and
  `Product.CreateAsync(…)` know which database to open.
- `Db.Configure(app.Services)` points the model surface at it, once, after the container exists. Without
  this pair the app builds and serves, and throws `The model database has not been configured` on the first
  line of data code.
- `AddDbContextFactory<AppDbContext>(…)` registers the context **as a factory**, not as a shared context.
  Rask pages are long-lived and can render concurrently, so every read and every write makes its own
  short-lived context instead of sharing one. `UseRaskSqlite` is a drop-in for `UseSqlite` that also applies the
  production pragmas (WAL, `busy_timeout`, `foreign_keys`) — so the app handles concurrent writers (the
  jobs, email, and outbox you add in later chapters) without hitting `database is locked`. It reads its
  connection string from `Rask:ConnectionStrings:App` — a local `app.db` in `appsettings.json` — which is
  why it takes the service provider, and a deploy overrides it with `Rask__ConnectionStrings__App`, which is
  how it points at a persistent volume.
- `AddRaskCqrs()` registers the dispatcher that the jobs and domain events of later chapters are handed
  through.

That factory is also what a page or a handler injects for work richer than one aggregate's write, such as
several aggregates changed in one transaction; see [Rask.Data](../data.md#writing-plain-ef-core).

## 7. Create the table

The code is ready, but `app.db` has no table for `Product` yet. EF Core **migrations** generate the schema
from your aggregates. `rask new` already created and applied the first one — the batteries' tables — and
`rask db` wraps the EF tooling for every one after it:

```bash
rask db add AddProduct        # generate a migration for what changed in the model
rask db update                # apply it — adds the Product table to app.db
```

`rask db add` writes into the `Migrations/` folder you commit alongside your code; `rask db update` runs it
against `app.db`. Every time you change an aggregate later, it's the same pair: `rask db add <Name>` then
`rask db update`.

## 8. Run it

```bash
rask dev
```

Browse to **`/products`**. You get a sortable, paged list with **New**, **Edit**, and **Delete** — each one
reading and writing SQLite through the `Product` type. Create a product and refresh: it's still there, because
it's on disk in `app.db`.

## Verify

- `Features/Products/` holds the aggregate and four components — no configuration class, no hand-written form
  model, and `AppDbContext` is untouched.
- The app builds with no warnings.
- After `rask db update`, `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).
- Open the same product's edit page in two tabs and save both: the second shows "Someone else changed
  this product" instead of overwriting the first.

> **Troubleshooting.** `rask db` can't find the project → make sure you `cd`'d into `Shop` first.
> `/products` fails with `no such table` → you skipped `rask db update`. The build can't find
> `Routes.ProductsPage()`, `ProductModel` or `Product.Where` → those are generated; build once and the IDE
> catches up. For a route, the generator also needs the `[Route]` attribute on the page.

**Learn more:** [Rask.Data](../data.md) · [forms](../forms.md) · [data grid](../data-grid.md) ·
[the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
