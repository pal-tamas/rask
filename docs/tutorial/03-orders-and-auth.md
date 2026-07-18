# Chapter 3 — A second feature, and locking it down

> **Goal:** add an **Orders** feature that shares the same database, then require a login to edit the catalog.
> **You'll run:** `rask generate feature Order … --context ProductsDbContext`

## 1. A second feature in the same database

The One-Person-Framework idea is *one* database for the whole product. So instead of letting the generator
create a second `DbContext`, we point the new feature at the one we already have with `--context`:

```bash
rask generate feature Order Total:decimal ProductId:guid Placed:datetime \
  --context ProductsDbContext --validation dataannotations
```

This writes `Features/Orders/` (entity, request, pages, CQRS handlers) **without** a new `DbContext`.

> **Relationships aren't generated yet.** You might expect `Order 1:n Product` — the CLI can parse that
> grammar, but it doesn't emit relationships today ([#479](https://github.com/pal-tamas/rask/issues/479)).
> So we model the link the simple way: a plain `ProductId:guid` field on `Order`. That's a normal foreign
> key; you just wire the navigation yourself if you want one.

### Add the DbSet by hand

Because we shared a context, the generator can't map `Order` on its own — it prints one line for you to add.
Open `Features/Products/ProductsDbContext.cs` and add the `Orders` set next to `Products`:

```csharp
public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();   // ← add this (and: using Shop.Features.Orders;)

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductsDbContext).Assembly);
        modelBuilder.ApplyRaskConventions();
    }
}
```

(`ApplyConfigurationsFromAssembly` already picks up the generated `OrderConfiguration`, so that's all.)

> **One more import.** Because `Order` shares the context that lives in the `Products` folder, the generated
> `Orders` files (`OrdersPage`, `CreateOrder`, …) reference `ProductsDbContext` across namespaces but don't
> import it yet ([#476](https://github.com/pal-tamas/rask/issues/476)). Add `using Shop.Features.Products;`
> to the top of those files — or, simplest, add a project-wide `global using Shop.Features.Products;` in a
> `GlobalUsings.cs` — so they compile.

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
Authorize[                                   // only rendered for signed-in users
    NavLink(CreateProduct())["New product"]
]
```

For role-specific bits — say a "Delete" button only admins should see — pass `Roles`:

```csharp
Authorize(Roles: ["admin"])[ DeleteProductButton(product.Id) ]
```

## Verify

- `/orders` renders and creating an order persists it (same `app.db` as products).
- Signed out, visiting `/products/new` redirects to `/login`; the "New product" button isn't shown on
  `/products`.
- After signing in (the `--auth` scaffold's `DemoCredentialStore` has demo users — see `Auth/CredentialStore.cs`),
  the create/edit/delete pages and buttons appear and work.

**Learn more:** [authentication](../authentication.md) · [the `rask` CLI](../cli.md)

Next → **[Chapter 4: Background jobs](04-background-jobs.md)**
