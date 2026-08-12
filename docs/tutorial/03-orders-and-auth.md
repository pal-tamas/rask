# Chapter 3 — A second feature, and locking it down

> **Goal:** add an **Orders** feature that shares the same database, then require a login to edit the catalog.
> **You'll write:** a second slice under `Features/Orders/`, sharing the one database.

## 1. A second feature in the same database

The One Person Framework idea is *one* database for the whole product — so a second feature does **not**
get a second `DbContext`. It maps through the one the app already has.

`Features/Orders/Order.cs` is chapter 2's shape with different fields:

```csharp
namespace Shop.Features.Orders;

public sealed class Order : Entity<Guid>
{
    private Order() { } // EF Core materialization

    private Order(decimal total, Guid productId, DateTime placed)
    {
        Id = Guid.NewGuid();
        this.Total = total;
        this.ProductId = productId;
        this.Placed = placed;
    }

    public decimal Total { get; private set; }

    public Guid ProductId { get; private set; }

    public DateTime Placed { get; private set; }

    public static Order Create(decimal total, Guid productId, DateTime placed) => new(total, productId, placed);

    public void Update(decimal total, Guid productId, DateTime placed)
    {
        this.Total = total;
        this.ProductId = productId;
        this.Placed = placed;
    }
}
```

Then the same four companions as before — `OrderRequest`, `OrderConfiguration`, the command/handler/page
files, and the list page. They're the chapter 2 files with `Product` swapped for `Order`; copy them and
change the type, or read them in the
[sample](https://github.com/pal-tamas/rask/tree/main/samples/Rask.Example.Shop/Features/Orders).

What ties it to the existing database goes in `Features/Shared/AppDbContext.cs`, next to `Products` —
the slice's namespace, and the set:

```csharp
using Shop.Features.Orders;   // at the top, beside the Products one
```
```csharp
public DbSet<Order> Orders => Set<Order>();   // inside the class
```

That's the whole of "sharing a database": one context, one connection string, one migration history,
however many features you add. Nothing else in the slice knows or cares.

> **Relating entities.** `Order.ProductId` is a plain foreign key here. To have EF understand it as a
> relationship, add a navigation property and map it in `OrderConfiguration`:
>
> ```csharp
> entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
> ```
>
> See [Rask.Data](../data.md) for the full relationship shapes.

### Migrate

A new table means a new migration:

```bash
rask db add AddOrder
rask db update
```

Run `rask dev` and browse to `/orders` — a second working CRUD feature, in the same `app.db`.

## 2. Require a login to edit the catalog

Right now anyone can create or delete products. Chapter 1's `--auth` gave us a login; let's use it. Rask
offers two gates, and you'll use both:

- **`[Authorize]`** on a page — route-level. An anonymous deep-link to the page gets redirected to `/login`.
- **The `Authorize` component** — content-level. It renders one of its slots depending on who's signed in,
  without leaving the page.

### Gate the write pages

Add `[Authorize]` (from `Microsoft.AspNetCore.Authorization`) to the generated create / edit / delete pages —
`Features/Products/CreateProduct.cs`, `UpdateProduct.cs`, `DeleteProduct.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

[Authorize]                          // ← anonymous users are redirected to /login
[Route("/products/new")]
public sealed class CreateProduct : Component { … }
```

Leave the read-only `ProductsPage` (`/products`) public so shoppers can browse.

### Hide the "New / Edit / Delete" buttons from anonymous users

Route gating stops direct navigation, but you also don't want to *show* buttons that will just bounce to the
login page. Wrap them in the `Authorize` component (from `Rask.Core.Components`), which the `--auth` scaffold
already uses in `Auth/MembersPage.cs`:

```csharp
Authorize[                                 // only rendered for signed-in users
    NavLink.Href(CreateProduct)["New product"]
]
```

For role-specific bits — say a "Delete" button only admins should see — pass `Roles`:

```csharp
Authorize.Roles(["admin"])[ DeleteProductButton(product.Id) ]
```

## Verify

- `/orders` renders and creating an order persists it (same `app.db` as products).
- Signed out, visiting `/products/new` redirects to `/login`; the "New product" button isn't shown on
  `/products`.
- After signing in (the `--auth` scaffold's `DemoCredentialStore` has demo users — see `Auth/CredentialStore.cs`),
  the create/edit/delete pages and buttons appear and work.

**Learn more:** [authentication](../authentication.md) · [the `rask` CLI](../cli.md)

Next → **[Chapter 4: Background jobs](04-background-jobs.md)**
