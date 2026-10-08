# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll write:** a vertical slice under `Features/Products/` — an aggregate and its pages — then run
> `rask db add`.

This chapter sets the pattern every later feature repeats — **aggregate → pages → migrate**. Do it once here
and the rest of the tutorial is variations on it.

Everything below is code you write. It's longer than the chapters that follow because it's the only one
that shows a slice end to end; once you've typed it, the shape is yours and later chapters only show
what's new.

> **You don't name a database.** Chapter 1's `rask new` already wired one. Rask's own context,
> `RaskAppDbContext`, maps every aggregate you declare, so there is no context to edit when you add one, and
> an app keeps **one** database and one set of migrations however many features you add.

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

They're ordinary read-only properties, so a page can show `product.CreatedAt` or sort by it. A delete removes the
row; an aggregate whose deleted rows must stay recoverable declares `public const Deletion Deletes = Deletion.Soft;`
and gets a `DeletedAt` column instead (see [choosing what a table carries](../data.md#choosing-what-a-table-carries)).

Two rules shape the class, and the build enforces both:

- **Setters are `private set`.** A public setter on an aggregate is a build error
  ([RASK084](../diagnostics.md#rask084)): state changes only through the aggregate's own code, so nothing outside
  can poke a field. EF Core and the forms below both write through private setters.
- **No constructor.** The implicit one is what EF Core builds rows with and what a new form starts from. When an
  aggregate needs a domain way to be created, it gets a static factory; chapter 7 gives `Order` one.

`[Required, MaxLength(200)]` is read twice: EF Core makes the column `NOT NULL` and 200 characters, and the
form checks it as the user types. `[Range]` is only for the form. `InStock`'s `= true` is the default a new
product starts with, on the create form too.

**Reading needs nothing more.** The build generates a **read face** beside the aggregate —
`Product.Where(…)`, `Product.AsQueryable()` — whose rows are `ProductRead`: plain columns, no
behaviour, nothing to save. Each read opens its own database context, runs, and disposes it before it
returns. That's what makes it safe to call straight from a page: a Rask page lives as long as the browser
keeps its socket open, and nothing here holds a context between calls.

Why a separate type to read from? Because an aggregate is a consistency *boundary*: it holds another
aggregate's id and never a navigation to it, so a write cannot cross a boundary by accident. The read face
has no such rule — it carries the joins those ids imply — and keeping the two apart is what lets both be
true at once. [The data guide](../data.md#reading-the-read-face) has the whole of it.

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
var product = await Product.Create(model);        // a new row with a new Id
await Product.Update(id, model);                  // only the changed columns; a stale Version throws
await Product.Delete(id, version);                // removes the row; a stale Version throws
```

Each one opens a context, saves through the interceptors `rask new` wired (so the timestamps and `Version` are
looked after), and disposes it. There's nothing to inject and nothing to write here: the next three sections are
just pages.

## 3. The create page

`Features/Products/CreateProduct.cs`:

```csharp
namespace Shop.Features.Products;

[Route("/products/new")]
public sealed partial class CreateProduct : Component
{
    private readonly ProductModel _model = new();

    protected override Component? HeadAssets => Title["New Product"];

    protected override Component? Render()
    {
        // The same command every render, so "saving" survives the re-render the click causes.
        var save = QueryClient.Command();

        return
        [
            Div.Class("flex flex-wrap items-center gap-3")[
                Ui.Heading.Level(1).Xl["New product"],
                Ui.Spacer,
                Ui.Button.Ghost.Href(Routes.ProductsPage())["Cancel"]
            ],
            Ui.Card[
                save.IsError ? Ui.Callout.Danger.Role("alert").Heading("Something went wrong — please try again.") : null,
                Form.Model(_model).OnSubmit(async model => await save.Send(async ct =>
                {
                    await Product.Create(model, cancellationToken: ct);
                    Routes.ProductsPage().Go();
                }, CancellationToken))[
                    Ui.Input.Bind(() => _model.Name).Label("Name"),
                    Ui.Input.Bind(() => _model.Price).Label("Price").Min("0").Step("0.01")
                        .Description("What a customer pays, before tax."),
                    Ui.Checkbox.Bind(() => _model.InStock).Label("In stock"),
                    Ui.Button.Primary.Submit.Disabled(save.IsPending)["Save"]
                ]
            ]
        ];
    }
}
```

`Routes.ProductsPage()` is generated from the `[Route]` on the list page you're about to write — a typed
URL, so renaming a route breaks the build instead of the link. See [routing](../routing.md).

The page is built from the [Rask.Ui kit](../ui-kit.md), so there isn't a class string in it. `Ui.Input` is
a whole field in one line: its label sits over the box (put guidance in `Description`, under the label,
rather than in a placeholder), and the field's own validation message appears under it.
`Ui.Button.Submit` is the form's submit button, and **Cancel** is a `Ui.Button` too. Given
`Href` it renders as a link, and because `Routes.ProductsPage()` is a generated route rather than a string,
the runtime follows it without reloading the page. See [the UI kit](../ui-kit.md#buttons-and-links-that-go-somewhere).

Nothing in that form mentions validation, and the attributes on `Product` are still enforced: `Form.Model(m)`
validates its model on its own, with no package to add and nothing to declare, and the save only runs for a
valid one. See [forms](../forms.md) and [validation](../validation.md).

The save goes through a **command**: `QueryClient.Command()` hands back the same command every render, and
sending work through it is what the page reads its state from. `IsPending` greys the button while the row is
written, so a double click can't create two products, and a failure lands on `IsError` instead of escaping the
click — `Send` never throws, which is why there is no `try` here. `Send` hands back a step that does
nothing until it is awaited, so the handler is `async` and awaits it; forget the `await` and
[RASK093](../diagnostics.md#rask093) stops the build rather than letting the save silently not happen.
A command also refreshes whatever a save made stale: the product count you'll put on the list page updates by itself once `Create` commits.
See [queries and commands](../query.md).

## 4. Edit and delete

Same shape, and the list page in the next step links to both — so write them now or it won't compile.

`Features/Products/UpdateProduct.cs` loads the row as a `ProductModel` and saves it back:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

[Route("/products/{id:guid}/edit")]
public sealed partial class UpdateProduct : Component
{
    // Loaded for this page alone: a zero GcTime drops it the moment you leave, so a later visit always
    // starts from the database, and the form can edit what was loaded in place.
    private static readonly QueryOptions ThisPageOnly = new() { GcTime = TimeSpan.Zero };

    private ProductModel? _model;
    private Guid _modelFor;

    [RouteParam] public Guid Id { get; set; }

    protected override Component? HeadAssets => Title["Edit Product"];

    protected override Component? Render()
    {
        var product = QueryClient.Query(QueryKey.For<Product>("edit"), Id,
            (id, ct) => Product.Model(id, cancellationToken: ct), ThisPageOnly);
        var save = QueryClient.Command();

        if (product.IsLoading)
        {
            return Ui.Loading.Text("Loading…");
        }

        if (product.Data is not { } loaded)
        {
            return Ui.Callout.Warning.Heading("Product not found.")[
                Ui.CalloutText[Ui.CalloutLink.Href(Routes.ProductsPage())["Back to the list"]]
            ];
        }

        // One copy per product, taken when it arrives: a refetch landing while you type must not reset the form.
        if (_model is null || _modelFor != Id)
        {
            _model = loaded;
            _modelFor = Id;
        }

        return
        [
            Div.Class("flex flex-wrap items-center gap-3")[
                Ui.Heading.Level(1).Xl["Edit product"],
                Ui.Spacer,
                Ui.Button.Ghost.Href(Routes.ProductsPage())["Cancel"]
            ],
            Ui.Card[
                save.Error switch
                {
                    null => null,
                    DbUpdateConcurrencyException => Ui.Callout.Danger.Role("alert")
                        .Heading("Someone else changed this product while you were editing it.")
                        .Text("Reload to see their changes."),
                    KeyNotFoundException => Ui.Callout.Danger.Role("alert").Heading("This product has been deleted."),
                    _ => Ui.Callout.Danger.Role("alert").Heading("Something went wrong — please try again."),
                },
                Form.Model(_model).OnSubmit(async model => await save.Send(async ct =>
                {
                    await Product.Update(Id, model, cancellationToken: ct);
                    Routes.ProductsPage().Go();
                }, CancellationToken))[
                    Ui.Input.Bind(() => _model.Name).Label("Name"),
                    Ui.Input.Bind(() => _model.Price).Label("Price").Min("0").Step("0.01")
                        .Description("What a customer pays, before tax."),
                    Ui.Checkbox.Bind(() => _model.InStock).Label("In stock"),
                    Ui.Button.Primary.Submit.Disabled(save.IsPending)["Save changes"]
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
  remembers the version the form was loaded at, and `Update` refuses to write if the row has moved on
  since, throwing `DbUpdateConcurrencyException` rather than overwriting someone else's edit. A row deleted
  in the meantime is a `KeyNotFoundException`.
- **The save writes what the form holds.** `Update` loads the row, copies the model onto it and saves only
  the columns that changed. The model is how a change gets back: nothing the page holds is tracked by EF.
- **The load is a query, and it follows the route.** `QueryClient.Query(key, Id, load)` is asked for in
  `Render`, where the route's `Id` is already bound, and the same call is the same query every render — so
  going from `/products/1/edit` to `/products/2/edit` re-points it at product 2 with nothing to call.
  `IsLoading` is the only state that needs a spinner, and the load is handed the `Id` its key was built from,
  so a slow load for product 1 can never land in product 2's form.

`Features/Products/DeleteProduct.cs` is a small reusable button the list page drops next to each row:

```csharp
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
    // Non-nullable, and still optional: a caller that leaves it off gets an unset callback that does nothing.
    public Callback OnDeleted { get; set; }

    protected override Component? Render()
    {
        // One button per row, and each row is its own DeleteProduct, so each has its own pending state.
        var delete = QueryClient.Command();

        return Ui.Button.Ghost.Red.Sm
            .Disabled(delete.IsPending)
            .OnClick(async () =>
            {
                // A row someone edited or deleted first fails here and lands on delete.Error; refreshing the
                // list below shows the reader what happened either way.
                await delete.Send(ct => Product.Delete(Id, Version, cancellationToken: ct), CancellationToken);

                // Tell the caller. Unset, this does nothing; a synchronous handler completes without a Task.
                await OnDeleted.Invoke();
            })[delete.IsPending ? "Deleting…" : "Delete"];
    }
}
```

A delete **removes the row** from `app.db`, so every read, the list's included, stops seeing it. `Product`
declares no `Deletes` const and takes that default; declaring `Deletion.Soft` would keep deleted products in the
table behind a `DeletedAt` stamp instead.

## 5. The list page

`Features/Products/ProductsPage.cs` — the routed page, over a data grid:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

[Route("/products")]
public sealed partial class ProductsPage : Component
{
    // An IQueryable, not a list. It holds no database connection: the grid runs it — sorted and paged in
    // SQL — each time it renders, and each run opens and disposes its own context.
    private readonly IQueryable<ProductRead> _products = Product.OrderBy(p => p.Name).AsQueryable();

    protected override Component? HeadAssets => Title["Products"];

    protected override Component? Render()
    {
        // Cached for this session, and refetched by itself after any Product write — a create, an edit, a
        // delete — because its key is about Product.
        var count = QueryClient.Query(QueryKey.For<Product>("count"), () => Product.Count());

        return
        [
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Heading.Level(1).Xl[count.Data is { } n ? $"Products ({n})" : "Products"],
            Ui.Spacer,
            Ui.Button.Primary.Href(Routes.CreateProduct())["New product"]
        ],
        Ui.DataGrid.Data(_products).RowKey(p => p.Id).PageSize(20).Label("Products")[c => [
            c.Field(p => p.Name).Title("Name").Sortable(),
            c.Field(p => p.Price).Title("Price").Sortable(),
            c.Field(p => p.InStock).Title("In stock"),
            c.Field(p => p.UpdatedAt).Title("Updated").Sortable(),
            c.Column().Title("Actions").Cell(p => Div[
                Ui.Button.Ghost.Sm.Href(Routes.UpdateProduct(p.Id))["Edit"],
                // The grid re-runs its query on every render, so asking for one is the whole refresh.
                DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(StateHasChanged)
            ]),
        ]]
        ];
    }
}
```

`Product.AsQueryable()` is a standard `IQueryable<ProductRead>` that holds no context, which is the
shape [`Ui.DataGrid`](../data-grid.md) wants: clicking a sortable header becomes `ORDER BY`, and the pager
becomes `Skip`/`Take`, so the database does the work however large the catalog grows. `RowKey` is required —
it is what the grid identifies a row by when it redraws. The grid shows read faces, read-only by
construction; `UpdatedAt` is one of the columns `Aggregate<Guid>` brought, sortable like any other.

Note what the page doesn't have: an `OnMount`. The grid runs its `IQueryable` when it renders, and the
count in the heading is a **query** — asked for in `Render`, cached for the session, loading on its own. Its
key, `QueryKey.For<Product>("count")`, says what it is about, and that is the whole of keeping it right: once
any `Product` write commits — `Create`, `Update`, `Delete` — every query about `Product` on this
session's screen refetches, so creating a product and coming back shows the new count with nothing written to
make it happen. The grid is left an `IQueryable` on purpose: it pages and sorts in SQL, which a cached list
could not. See [queries and commands](../query.md).

## 6. Already registered

There's nothing to register. `RaskApp.Create(args)` in `Program.cs` turned the database on, and it reads
where the database is from `appsettings.json`:

```jsonc
"Rask": {
  "ConnectionStrings": {
    "App": "Data Source=app.db"
  }
}
```

What that one line did for this slice:

- It registered `RaskAppDbContext` **as a factory**, not as a shared context. Rask pages are long-lived and
  can render concurrently, so every read and every write makes its own short-lived context instead of
  sharing one. The context opens SQLite with the production pragmas (WAL, `busy_timeout`, `foreign_keys`), so
  the app handles concurrent writers — the jobs, email and outbox of later chapters — without hitting
  `database is locked`. A deploy overrides the connection string with `Rask__ConnectionStrings__App`, which
  is how it points at a persistent volume.
- It pointed the model surface at that context, which is what lets `Product.Where(…)` and
  `Product.Create(…)` open a database with nothing injected.
- It added the interceptors that fill in timestamps and versions, publish events, and soft-delete an
  aggregate that asks for it.

That factory is also what a page or a handler injects for work richer than one aggregate's write, such as
several aggregates changed in one transaction: `IDbContextFactory<RaskAppDbContext>`. See
[Rask.Data](../data.md#writing-plain-ef-core).

## 7. Create the table

The code is ready, but `app.db` has no table for `Product` yet. EF Core **migrations** generate the schema
from your aggregates. `rask new` already created the first one — the batteries' tables — and `rask db` wraps
the EF tooling for every one after it:

```bash
rask db add AddProduct        # generate a migration for what changed in the model
```

`rask db add` writes into the `Migrations/` folder you commit alongside your code. There is no second
command: the app applies its pending migrations when it starts, before it serves a page. Every time you change
an aggregate later, it's the same: `rask db add <Name>`, then (re)start the app.

## 8. Run it

```bash
rask dev
```

Browse to **`/products`**. You get a sortable, paged list with **New**, **Edit**, and **Delete** — each one
reading and writing SQLite through the `Product` type. Create a product and refresh: it's still there, because
it's on disk in `app.db`.

## Verify

- `Features/Products/` holds the aggregate and four components — no configuration class, no hand-written form
  model, and no context to edit.
- The app builds with no warnings.
- After `rask db add AddProduct` and a start, `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).
- Open the same product's edit page in two tabs and save both: the second shows "Someone else changed
  this product" instead of overwriting the first.

> **Troubleshooting.** `rask db` can't find the project → make sure you `cd`'d into `Shop` first.
> `/products` fails with `no such table` → you skipped `rask db add AddProduct`, or the app was already running
> when you added it — restart it so it applies the migration. The build can't find
> `Routes.ProductsPage()`, `ProductModel` or `Product.Where` → those are generated; build once and the IDE
> catches up. For a route, the generator also needs the `[Route]` attribute on the page.

**Learn more:** [Rask.Data](../data.md) · [forms](../forms.md) · [data grid](../data-grid.md) ·
[the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
