# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll write:** a vertical slice under `Features/Products/` — an entity, its commands and its pages — then
> run `rask db add` / `rask db update`.

This chapter sets the pattern every later feature repeats — **entity → commands → pages → migrate**. Do it once here
and the rest of the tutorial is variations on it.

Everything below is code you write. It's longer than the chapters that follow because it's the only one
that shows a slice end to end; once you've typed it, the shape is yours and later chapters only show
what's new.

> **You don't name a database.** Chapter 1's `rask new` already wired one — `AppDbContext` in
> `Features/Shared/`. Its base, `RaskDbContext`, maps every entity you declare, so you never edit it to add
> one, and an app keeps **one** database and one set of migrations however many features you add.

## 1. The entity

`Features/Products/Product.cs`. The constructor is private and the setters are `private set`, so nothing
outside can build a `Product` halfway or poke a field — the ways in are the factory and the method the
entity declares itself:

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

    public static Product Create(string name, decimal price, bool inStock)
    {
        var product = new Product { Id = Guid.CreateVersion7() };
        product.Change(name, price, inStock);
        return product;
    }

    public void Change(string name, decimal price, bool inStock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        Name = name.Trim();
        Price = price;
        InStock = inStock;
    }
}
```

`Model<Guid>` comes from [Rask.Data](../data.md). It supplies the `Id`, and mapping the class needs
nothing else — no `DbSet` property, no configuration class, no registration. `[Required, MaxLength(200)]`
is read by EF Core as the column (`NOT NULL`, 200 characters). The `private set`s are this tutorial's
choice, not a requirement: public setters compile and save just the same, and the build only points them out
with a warning (RASK084). Keeping them private means the state changes through the entity's own methods —
`Create` and `Change` here — which is where the rules live: a blank name or a negative price can't reach the
database, whoever calls.

The two markers are opt-in, and opting in costs nothing in the class:

- **`ITimestamped`** — `CreatedAt`/`UpdatedAt` columns exist and are filled in on every save, without
  appearing on the type. Declare `public DateTime CreatedAt { get; private set; }` only when a screen
  needs to show it.
- **`IVersioned`** — a concurrency token, bumped on every update. It is the one marker that must declare
  its property, because an edit form has to carry the version it was loaded at. It's what stops two people
  editing the same product from silently overwriting each other — you'll see it at work in section 4.

**Reading needs nothing more.** `Product.Where(…)`, `Product.FindAsync(id)` and `Product.AsQueryable()` are
on the type already. Each opens its own database context, runs, and disposes it before it returns, and
hands back rows nothing is tracking. That's what makes it safe to call them straight from a page: a Rask
page lives as long as the browser keeps its socket open, and nothing here holds a context between calls.

## 2. The writes: one command per change

Reads are on the type, and so are the plain writes — `Product.CreateAsync(model)`, `Product.UpdateAsync(id, model)`,
`Product.DeleteAsync(id)` ([Rask.Data](../data.md#writing-create-update-delete)). This chapter builds the shape a
change grows into instead: a **command** — a message saying what to do — and a **handler** that does it: loads the
entity, calls its method, saves. `Features/Products/ProductCommands.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shop.Features.Shared;

namespace Shop.Features.Products;

// What the create form fills in. Mutable, because a form binds to it while someone types.
public sealed class AddProduct : ICommand<Guid>
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Range(0, 1_000_000)]
    public decimal Price { get; set; }

    public bool InStock { get; set; }
}

public sealed class AddProductHandler(IDbContextFactory<AppDbContext> contexts)
    : ICommandHandler<AddProduct, Guid>
{
    public async Task<Guid> HandleAsync(AddProduct command, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var product = Product.Create(command.Name, command.Price, command.InStock);
        db.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return product.Id;
    }
}

// What the edit form fills in, starting from the row it edits.
public sealed class EditProduct : ICommand
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Range(0, 1_000_000)]
    public decimal Price { get; set; }

    public bool InStock { get; set; }

    // The version the form was loaded at.
    public int Version { get; set; }

    public static EditProduct From(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Price = product.Price,
        InStock = product.InStock,
        Version = product.Version,
    };
}

public sealed class EditProductHandler(IDbContextFactory<AppDbContext> contexts)
    : ICommandHandler<EditProduct>
{
    public async Task HandleAsync(EditProduct command, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var product = await db.Set<Product>().FindAsync([command.Id], cancellationToken)
                      ?? throw new KeyNotFoundException($"There is no product {command.Id}.");

        // Compare against the version the form was loaded at, not the one just read, so a save that
        // lost a race throws DbUpdateConcurrencyException instead of overwriting the winner.
        db.Entry(product).Property(p => p.Version).OriginalValue = command.Version;
        product.Change(command.Name, command.Price, command.InStock);

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record RemoveProduct(Guid Id, int Version) : ICommand;

public sealed class RemoveProductHandler(IDbContextFactory<AppDbContext> contexts)
    : ICommandHandler<RemoveProduct>
{
    public async Task HandleAsync(RemoveProduct command, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var product = await db.Set<Product>().FindAsync([command.Id], cancellationToken)
                      ?? throw new KeyNotFoundException($"There is no product {command.Id}.");

        db.Entry(product).Property(p => p.Version).OriginalValue = command.Version;
        db.Remove(product);

        await db.SaveChangesAsync(cancellationToken);
    }
}
```

Three things make this the shape every later write repeats:

- **The command is the form model.** `AddProduct` and `EditProduct` are what the pages bind their inputs
  to, so their `[Required]`, `[MaxLength]` and `[Range]` are checked as the user types — and again by the
  dispatcher before the handler runs, so a command sent from anywhere else is held to the same rules. The
  entity's own guards are the last line: they hold even for code that skips both. (The build also generates
  a `ProductModel` beside `Product` — the entity's properties and attributes as a form shape — for a page
  that would rather bind that; see [a create and an edit form](../data.md#a-create-and-an-edit-form).)
- **The handler makes its own context.** `IDbContextFactory<AppDbContext>`, not a context injected
  directly: a page dispatching in-process shares its live session's services, and a session outlives any
  unit of work. One context per command, disposed when the command is done.
- **The save runs the conventions.** `SaveChangesAsync` goes through the interceptors `rask new` wired, so
  `CreatedAt`/`UpdatedAt` are stamped and `Version` is bumped without a line of it here.

Nothing registers the handlers — they're found at build time. See [CQRS](../cqrs.md) for the rest of what
the dispatcher does.

## 3. The create page

`Features/Products/CreateProduct.cs`:

```csharp
using Rask.Core.Routing;

namespace Shop.Features.Products;

[Route("/products/new")]
public sealed partial class CreateProduct(IDispatcher dispatcher, Navigator navigator) : Component
{
    private readonly AddProduct _model = new();
    private string? _error;

    protected override Component? HeadAssets => Title["New Product"];

    private async Task SaveAsync(AddProduct model)
    {
        try
        {
            await dispatcher.SendAsync(model, CancellationToken);
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

Nothing in that form mentions validation, and the attributes on `AddProduct` are still enforced:
`Form<T>` validates its model on its own, with no package to add and nothing to declare, and `SaveAsync`
only runs for a valid one. `dispatcher.SendAsync(model)` hands back the new product's id — `AddProduct` is
an `ICommand<Guid>`. See [forms](../forms.md) and [validation](../validation.md).

## 4. Edit and delete

Same shape, and the list page in the next step links to both — so write them now or it won't compile.

`Features/Products/UpdateProduct.cs` loads the row, turns it into an `EditProduct`, and sends it back:

```csharp
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;

namespace Shop.Features.Products;

[Route("/products/{id:guid}/edit")]
public sealed partial class UpdateProduct(IDispatcher dispatcher, Navigator navigator) : Component
{
    private EditProduct _model = new();
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
            _model = EditProduct.From(product);
        }

        _loaded = true;
    }

    private async Task SaveAsync(EditProduct model)
    {
        try
        {
            await dispatcher.SendAsync(model, CancellationToken);
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

- **The row comes from the route.** The page looks the product up by its `[RouteParam]`, and
  `EditProduct.From` copies that row's id — no input on the form carries it.
- **`From` copies the `Version` too.** That is the whole of optimistic concurrency here: the command
  remembers the version the form was loaded at, and the handler refuses to write if the row has moved on
  since, throwing `DbUpdateConcurrencyException` rather than overwriting someone else's edit. A row deleted
  in the meantime is a `KeyNotFoundException`.
- **The row `FindAsync` returns is untracked.** Changing it and hoping it saves would do nothing — which is
  exactly why the change goes through a command, whose handler loads the entity into the context that saves
  it.
- **`OnPropsChangedAsync`, not a constructor or `OnInitialized`.** A Rask component loads its data from its
  lifecycle hooks: `OnMountAsync` once, `OnPropsChangedAsync` whenever its props — here the route's `Id` —
  change. The [lifecycle](../lifecycle.md) guide has the full order.

`Features/Products/DeleteProduct.cs` is a small reusable button the list page drops next to each row:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Shop.Features.Products;

// A reusable delete button: removes the product, then invokes OnDeleted so the caller (the list page)
// can refresh.
public sealed partial class DeleteProduct(IDispatcher dispatcher) : Component
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
            await dispatcher.SendAsync(new RemoveProduct(Id, Version), CancellationToken);
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
        UiHeader.Heading("Products").Actions(UiButton.Tone(UiTone.Primary).Href(Routes.CreateProduct())["New product"]),
        UiDataGrid.Data(_products).RowKey(p => p.Id).PageSize(20).Label("Products")[c => [
            c.Field(p => p.Name).Title("Name").Sortable(true),
            c.Field(p => p.Price).Title("Price").Sortable(true),
            c.Field(p => p.InStock).Title("In stock"),
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
is what the grid identifies a row by when it redraws.

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

- `AddRaskCqrs()` registers the dispatcher and every handler the build found — `AddProductHandler` and its
  two siblings included — and the validation that runs before each one.
- `AddRaskData<AppDbContext>()` registers the interceptors (auditing, and later soft-delete, concurrency
  and events) **and names the context to the model surface**. The type argument is what makes
  `Product.Where(…)` and `Product.FindAsync(…)` know which database to open.
- `Db.Configure(app.Services)` points the model surface at it, once, after the container exists. Without
  this pair the app builds and serves, and throws `The model database has not been configured` on the first
  line of data code.
- `AddDbContextFactory<AppDbContext>(…)` registers the context **as a factory**, not as a shared context.
  Rask pages are long-lived and can render concurrently, so every read and every handler makes its own
  short-lived context instead of sharing one. `UseRaskSqlite` is a drop-in for `UseSqlite` that also applies the
  production pragmas (WAL, `busy_timeout`, `foreign_keys`) — so the app handles concurrent writers (the
  jobs, email, and outbox you add in later chapters) without hitting `database is locked`. It reads its
  connection string from `Rask:ConnectionStrings:App` — a local `app.db` in `appsettings.json` — which is
  why it takes the service provider, and a deploy overrides it with `Rask__ConnectionStrings__App`, which is
  how it points at a persistent volume.

That factory is also what a page injects when a write is too small to deserve a command — one button that
saves one change. [Chapter 7](07-outbox-events.md)'s **Buy** button does exactly that.

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
reading SQLite through the model and writing it through a command. Create a product and refresh: it's still there, because it's
on disk in `app.db`.

## Verify

- `Features/Products/` holds the entity, its three commands and four components — no configuration class,
  and `AppDbContext` is untouched.
- The app builds with no warnings.
- After `rask db update`, `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).
- Open the same product's edit page in two tabs and save both: the second shows "Someone else changed
  this product" instead of overwriting the first.

> **Troubleshooting.** `rask db` can't find the project → make sure you `cd`'d into `Shop` first.
> `/products` fails with `no such table` → you skipped `rask db update`. The build can't find
> `Routes.ProductsPage()` or `Product.Where` → those are generated; build once and the IDE catches up. For a
> route, the generator also needs the `[Route]` attribute on the page.

**Learn more:** [Rask.Data](../data.md) · [CQRS](../cqrs.md) · [forms](../forms.md) ·
[data grid](../data-grid.md) · [the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
