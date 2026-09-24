# Chapter 3 — A second feature, and locking it down

> **Goal:** add an **Orders** feature that shares the same database, then require a login to edit the catalog.
> **You'll write:** a second slice under `Features/Orders/`, sharing the one database.

## 1. A second feature in the same database

The One Person Framework idea is *one* database for the whole product — so a second feature does **not**
get a second `DbContext`. It is mapped through the one the app already has.

`Features/Orders/Order.cs` is chapter 2's shape with different fields:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Shop.Features.Orders;

public sealed class Order : Aggregate<Guid>
{
    [Range(0, 1_000_000)]
    public decimal Total { get; private set; }

    public Guid ProductId { get; private set; }

    public DateTime Placed { get; private set; }
}
```

`Order` reads and writes the way `Product` does (`Order.Read.Where(…)`, `Order.CreateAsync(model)`,
`Order.Read.AsQueryable()`) and gets its own generated `OrderModel`, with nothing to build.

Look at `ProductId`. That is how an aggregate points at another one — an **id, never a navigation**, which is
what stops a save here reaching into the catalogue ([RASK087](../diagnostics.md#rask087) makes it a compile
error). You lose nothing by it: because `Product` is an aggregate with a `Guid` key, the build reads that id
and puts the join on the read face for you, named after the property:

```csharp
await Order.Read.Where(o => o.Product.Name == "Anvil").ToListAsync();   // declared nothing
```

Then the same four
components as before: `CreateOrder`, `UpdateOrder`, `DeleteOrder` and `OrdersPage`. They're the chapter 2 files
with `Product` swapped for `Order`, the routes moved under `/orders`, and the three inputs and grid columns
changed to `Total`, `ProductId` and `Placed`. Copy them and change the names.

`ProductId` is a `Guid`, not a `Product`. One aggregate refers to another **by id**: each is loaded and saved on
its own, so an order never drags a product along with it.

What ties the slice to the existing database: nothing you write. `AppDbContext`'s base maps `Order` exactly
as it maps `Product`, with no `DbSet` to add. That's the whole of "sharing a database": one context, one
connection string, one migration history, however many features you add. Nothing else in the slice knows
or cares.

> **Relating aggregates.** `Order.ProductId` is a plain foreign key here. To have EF understand it as a
> relationship, give `Order` a static `Configure` — the place for any mapping rule the conventions don't
> cover (it needs `using Microsoft.EntityFrameworkCore;`, `using Microsoft.EntityFrameworkCore.Metadata.Builders;`
> and `using Shop.Features.Products;`):
>
> ```csharp
> public static void Configure(EntityTypeBuilder<Order> builder) =>
>     builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
> ```
>
> It is found by its signature and runs after Rask's conventions. See [Rask.Data](../data.md) for the full
> relationship shapes.

### Migrate

A new table means a new migration:

```bash
rask db add AddOrder
rask db update
```

Run `rask dev` and browse to `/orders` — a second working CRUD feature, in the same `app.db`.

## 2. Require a login to edit the catalog

Right now anyone can create or delete products. The app already has accounts and a login; let's use them. Rask
offers two gates, and you'll use both:

- **`[Authorize]`** on a page — route-level. An anonymous deep-link to the page gets redirected to `/login`.
- **The `Authorize` component** — content-level. It renders one of its slots depending on who's signed in,
  without leaving the page.

### Gate the write pages

Add `[Authorize]` (from `Microsoft.AspNetCore.Authorization`) to the two write pages —
`Features/Products/CreateProduct.cs` and `UpdateProduct.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

[Authorize]                          // ← anonymous users are redirected to /login
[Route("/products/new")]
public sealed partial class CreateProduct : Component { … }
```

Leave the read-only `ProductsPage` (`/products`) public so shoppers can browse. `DeleteProduct` isn't a
page — it has no URL anyone could deep-link to — so it's gated the other way, by not rendering it.

### Hide the "New / Edit / Delete" buttons from anonymous users

Route gating stops direct navigation, but you also don't want to *show* buttons that will just bounce to the
login page. Wrap them in the `Authorize` component (from `Rask.Core.Components`):

```csharp
Ui.Header.Heading("Products").Actions(
    Authorize[                             // only rendered for signed-in users
        Ui.Button.Tone(Ui.Tone.Primary).Href(Routes.CreateProduct())["New product"]
    ])
```

For role-specific bits — say a "Delete" button only admins should see — pass `Roles`. In the grid's
actions column that is:

```csharp
Authorize.Roles(["admin"])[
    DeleteProduct.Id(p.Id).Version(p.Version).OnDeleted(StateHasChanged)
]
```

A button that is never rendered has no click handler on the page, so hiding it is a real gate on a live
page, not just a cosmetic one.

## Verify

- `/orders` renders and creating an order persists it (same `app.db` as products).
- Signed out, visiting `/products/new` redirects to `/login`; the "New product" button isn't shown on
  `/products`.
- After signing in (register an account first at `/register` — the first one is the administrator),
  the create/edit/delete pages and buttons appear and work.

**Learn more:** [authentication](../authentication.md) · [Rask.Data](../data.md) · [the `rask` CLI](../cli.md)

Next → **[Chapter 4: Background jobs](04-background-jobs.md)**
