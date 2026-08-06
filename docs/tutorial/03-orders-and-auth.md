# Chapter 3 — A second feature, and locking it down

> **Goal:** add an **Orders** feature that shares the same database, then require a login to edit the catalog.
> **You'll run:** `rask generate feature Order …`

## 1. A second feature in the same database

The One Person Framework idea is *one* database for the whole product — so the generator never creates a
second `DbContext`. It attaches this feature to the one the app already has:

```bash
rask generate feature Order Total:decimal ProductId:guid Placed:datetime \
  --validation dataannotations
```

This writes `Features/Orders/` (entity, request, pages, CQRS handlers) and no new `DbContext`. The CLI
reports:

```
Added 1 DbSet(s) to Features/Shared/AppDbContext.cs.
```

It found `AppDbContext` by scanning the project, added `public DbSet<Order> Orders => Set<Order>();` next to
`Products` (with the `using` it needs), and the generated `Orders` pages import the context's namespace — so it
compiles as-is, no hand-edits. Your one `AppDbContext` now holds both `Products` and `Orders`: one database,
one context. (If an app really does have several contexts, the CLI stops and asks which one with
`--context <Name>` rather than guessing.)

> **Relating entities.** When you scaffold related entities *together* in one command, Rask generates the
> foreign key, the navigation properties, and the EF mapping for you — e.g.
> `rask generate feature Post Title:string 1:n Comment Body:text` gives `Comment` a `PostId` + `Post` and
> `Post` a `Comments` collection (`n:1`, `1:1`, and `n:n` work too). Here, though, `Product` already exists
> from Chapter 2, so we just add a plain `ProductId:guid` field to `Order` — a normal foreign key you can
> wire a navigation onto yourself.

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
Authorize()[                                 // only rendered for signed-in users
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
