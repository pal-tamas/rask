# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll write:** a vertical slice under `Features/Products/`, then run `rask db add` / `rask db update`.

This chapter sets the pattern every later feature repeats — **entity → mapping → commands → pages →
migrate**. Do it once here and the rest of the tutorial is variations on it.

Everything below is code you write. It's longer than the chapters that follow because it's the only one
that shows a slice end to end; once you've typed it, the shape is yours and later chapters only show
what's new. The finished version of this app is committed as
[`samples/Rask.Example.Shop`](https://github.com/pal-tamas/rask/tree/main/samples/Rask.Example.Shop) if
you'd rather read it whole.

> **You don't name a database.** Chapter 1's `--all-batteries` already wired one — `AppDbContext` in
> `Features/Shared/`. Everything here maps through it, so an app keeps **one** database and one set of
> migrations however many features you add.

## 1. The entity

`Features/Products/Product.cs`. The constructor is private and the setters are `private set`, so a
`Product` can't be built halfway — the only ways in are `Create` and `Update`:

```csharp
namespace Shop.Features.Products;

public sealed class Product : Entity<Guid>
{
    private Product() { } // EF Core materialization

    private Product(string name, decimal price, bool inStock)
    {
        Id = Guid.NewGuid();
        this.Name = name;
        this.Price = price;
        this.InStock = inStock;
    }

    public string Name { get; private set; } = "";

    public decimal Price { get; private set; }

    public bool InStock { get; private set; }

    public static Product Create(string name, decimal price, bool inStock) => new(name, price, inStock);

    public void Update(string name, decimal price, bool inStock)
    {
        this.Name = name;
        this.Price = price;
        this.InStock = inStock;
    }
}
```

`Entity<Guid>` comes from [Rask.Data](../data.md). It supplies the `Id` and the audit fields
(`CreatedAt`/`UpdatedAt`) that the interceptors fill in for you.

## 2. The form model

`Features/Products/ProductRequest.cs`. Kept separate from the entity so the form can be half-filled and
invalid while the entity never is:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Shop.Features.Products;

// The shared form model for the create + edit slices; maps onto Product.Create/Update.
public sealed class ProductRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool InStock { get; set; }
}
```

## 3. The EF Core mapping

`Features/Products/ProductConfiguration.cs`. Persistence details live here rather than as attributes on
the entity, so the domain model stays free of EF:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Features.Shared;

namespace Shop.Features.Products;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
    }
}
```

Then add the set to `Features/Shared/AppDbContext.cs`:

```csharp
public DbSet<Product> Products => Set<Product>();
```

## 4. A command and its handler

`Features/Products/CreateProduct.cs` holds three things that belong together: the command, the handler
that owns the EF access, and the page that dispatches it.

```csharp
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Shop.Features.Shared;

namespace Shop.Features.Products;

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
```

The handler takes an `IDbContextFactory`, not a `DbContext`. Rask pages are long-lived and can render
concurrently, so each unit of work makes its own short-lived context instead of sharing one.

And the page that uses it, in the same file:

```csharp
[Route("/products/new")]
public sealed class CreateProduct(IDispatcher dispatcher, Navigator navigator) : Component
{
    private readonly ProductRequest _form = new();
    private string? _error;

    protected override Component? Head => Title()["New Product"];

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
        Div()[
            H1()["New Product"],
            _error is null ? null : Div(Role: "alert")[_error],
            Form(_form, OnValidSubmitAsync: SubmitAsync)[
                DataAnnotationsValidator(),
                Div()[Label("name")["Name"], Input(() => _form.Name, Id: "name")],
                Div()[Label("price")["Price"], Input(() => _form.Price, Id: "price")],
                Div()[Label("instock")["InStock"], Input(() => _form.InStock, Id: "instock")],
                Div()[
                    NavLink(Routes.ProductsPage())["Cancel"],
                    Button("submit")["Save"]
                ]
            ]
        ];
}
```

`Routes.ProductsPage()` is generated from the `[Route]` on the list page you're about to write — a typed
URL, so renaming a route breaks the build instead of the link. See [routing](../routing.md).

Note `OnMountAsync`, not a constructor or `OnInitialized`: a Rask component loads its data when it
mounts. The [lifecycle](../lifecycle.md) guide has the full order.

**`UpdateProduct.cs` and `DeleteProduct.cs` follow the same shape:** a record command, a handler that
loads and mutates through `Product.Update` (or removes), and — for update — an edit page that loads the
entity into a `ProductRequest` first. Write them by copying `CreateProduct.cs` and changing the verb, or
read them in the [sample](https://github.com/pal-tamas/rask/tree/main/samples/Rask.Example.Shop/Features/Products).

## 5. The list page

`Features/Products/ProductsPage.cs` — a query, its handler, and the routed page:

```csharp
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Shop.Features.Shared;

namespace Shop.Features.Products;

public sealed record ListProductsQuery : IQuery<IReadOnlyList<Product>>;

public sealed class ListProductsQueryHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : IQueryHandler<ListProductsQuery, IReadOnlyList<Product>>
{
    public async Task<IReadOnlyList<Product>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Products.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
    }
}

[Route("/products")]
public sealed class ProductsPage(IDispatcher dispatcher) : Component
{
    private IReadOnlyList<Product> _items = [];
    private bool _loaded;

    protected override Component? Head => Title()["Products"];

    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _items = await dispatcher.DispatchAsync(new ListProductsQuery(), CancellationToken);
        _loaded = true;
    }

    protected override Component? Render() =>
    [
        Div()[
            H1()["Products"],
            NavLink(Routes.CreateProduct())["New Product"]
        ],
        !_loaded
            ? Div()["Loading…"]
            : _items.Count == 0
                ? Div()["No Products yet."]
                : Table()[
                    Thead()[Tr()[Th()["Name"], Th()["Price"], Th()["InStock"], Th()[""]]],
                    Tbody()[
                        _items.Select(x => Tr(Key: x.Id)[
                            Td()[x.Name],
                            Td()[$"{x.Price}"],
                            Td()[$"{x.InStock}"],
                            Td()[
                                NavLink(Routes.UpdateProduct(x.Id))["Edit"],
                                DeleteProduct(Id: x.Id, OnDeleted: LoadAsync)
                            ]
                        ])
                    ]
                ]
    ];
}
```

## 6. Register the services

`Program.cs` needs three registrations (Chapter 1's `--all-batteries` already added them; if you
scaffolded without it, add them next to your other `builder.Services…` lines):

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

- `AddRaskCqrs()` registers the mediator that dispatches the queries/commands in the slice.
- `AddRaskData()` registers the interceptors (auditing, and later soft-delete/concurrency/events).
- `AddDbContextFactory<AppDbContext>(…)` registers the context **as a factory**, for the reason above.
  `UseRaskSqlite` is a drop-in for `UseSqlite` that also applies the production pragmas (WAL,
  `busy_timeout`, `foreign_keys`) — so the app handles concurrent writers (the jobs, email, and outbox you
  add in later chapters) without hitting `database is locked`. It defaults to a local `app.db` file next to
  the app but honours a `ConnectionStrings:App` override, which is how a deploy points it at a persistent
  volume.

## 7. Create the database

The code is ready, but the SQLite file has no tables yet. EF Core **migrations** generate the schema from
your entities. `rask db` wraps the EF tooling (installing `dotnet-ef` for you on first use):

```bash
rask db add InitialCreate     # generate a migration from the current model
rask db update                # apply it — creates app.db with a Products table
```

`rask db add` writes a `Migrations/` folder you commit alongside your code; `rask db update` runs it
against `app.db`. Every time you change an entity later, it's the same pair: `rask db add <Name>` then
`rask db update`.

## 8. Run it

```bash
rask dev
```

Browse to **`/products`**. You get a working list page with **New**, **Edit**, and **Delete** — each
button dispatching a real CQRS command that reads or writes SQLite. Create a product and refresh: it's
still there, because it's on disk in `app.db`.

## Verify

- `Features/Products/` holds the entity, the request, the configuration, and the pages.
- `AppDbContext` has a `DbSet<Product>` and the app builds.
- After `rask db update`, an `app.db` file exists and `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).

> **Troubleshooting.** `rask db` can't find the project → make sure you `cd`'d into `Shop` first.
> `rask db update` fails with "no migrations" → you skipped `rask db add`. The build can't find
> `Routes.ProductsPage()` → the route generator needs the `[Route]` attribute on `ProductsPage` and a
> successful build of that file first.

**Learn more:** [data access](../data-access.md) · [Rask.Data](../data.md) · [CQRS](../cqrs.md) ·
[the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
