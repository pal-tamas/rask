# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll run:** `rask generate feature Product …`, then `rask db add` / `rask db update`.

This is the most important chapter. Once you've done it once, every other feature is the same three moves:
**generate → wire → migrate.**

> **`rask new` gives you no database on purpose.** The starter app has no `DbContext` and no SQLite. The
> database arrives the moment you generate your first feature — the generator adds the packages, writes the
> data code, and tells you the one block to paste into `Program.cs`. So there's nothing to "set up" first;
> just generate.

## 1. Generate the feature

From inside the `Shop` folder (the CLI finds the project by walking up from where you are, so `cd` in first):

```bash
rask generate feature Product Name:string Price:decimal InStock:bool --validation dataannotations
```

Read the field list as `name:type`. Rask understands `string`, `int`, `long`, `decimal`, `double`, `bool`,
`datetime`, `date`, `time`, and `guid`. A trailing `?` makes a field optional (`Description:string?`), and
`string(100)` caps a string's length. `Id` is added for you — don't list it.

> **`--validation dataannotations`** keeps the form validation familiar: `[Required]`, `[MaxLength]`, and a
> `DataAnnotationsValidator()` on the form. Leave it off and Rask's default (`valueobjects`) wraps each
> required string in a small DDD value-object type instead — nice for a real domain, more than a first
> feature needs. See [Rask.Data](../data.md) when you want it.
>
> **Quote fields that contain `?` or `()`** so your shell doesn't try to interpret them:
> `"Description:string?"`.

The generator writes a complete vertical slice into `Features/Products/`:

| File | What it is |
|------|-----------|
| `Product.cs` | the entity — an `Entity<Guid>` with a private constructor and `Create` / `Update` factory methods, so it can't be built in an invalid state |
| `ProductRequest.cs` | the form model the create/edit pages bind to (with the DataAnnotations) |
| `ProductConfiguration.cs` | the EF Core `IEntityTypeConfiguration<Product>` (column types, lengths) |
| `ProductsDbContext.cs` | a `DbContext` with a `DbSet<Product>` |
| `ProductsPage.cs` | the routed list page at `/products`, plus a `ListProductsQuery` + handler ([CQRS](../cqrs.md)) |
| `CreateProduct.cs`, `UpdateProduct.cs` | the new / edit pages, each with its own command + handler |
| `DeleteProduct.cs` | the delete command + a reusable delete button |

It also **adds the packages** the slice needs (EF Core + SQLite, `Rask.Cqrs`, `Rask.Data`) and restores.

Open `Features/Products/Product.cs` and `ProductsPage.cs` now — the generated code is the best documentation
of the patterns this tutorial uses.

## 2. Wire it into `Program.cs`

The one thing the generator can't do for you is edit `Program.cs` (it's your composition root). When it
finishes it prints the exact block to add — paste it into `Program.cs`, next to the other
`builder.Services…` lines:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData();
builder.Services.AddDbContextFactory<ProductsDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

- `AddRaskCqrs()` registers the mediator that dispatches the queries/commands in the slice.
- `AddRaskData()` registers the interceptors (auditing, and later soft-delete/concurrency/events).
- `AddDbContextFactory<ProductsDbContext>(…)` registers the context **as a factory** — Rask pages are
  long-lived and may render concurrently, so each unit of work creates its own short-lived context rather
  than sharing one scoped instance. `Data Source=app.db` is a SQLite file created next to the app.

Add the `using` directives the IDE prompts for — `Microsoft.EntityFrameworkCore` (for `UseSqlite` /
`AddInterceptors`), `Microsoft.EntityFrameworkCore.Diagnostics` (for `ISaveChangesInterceptor`), and your
`Shop.Features.Products` namespace.

## 3. Create the database

The code is ready, but the SQLite file has no tables yet. EF Core **migrations** generate the schema from
your entities. `rask db` wraps the EF tooling (installing `dotnet-ef` for you on first use):

```bash
rask db add InitialCreate     # generate a migration from the current model
rask db update                # apply it — creates app.db with a Products table
```

`rask db add` writes a `Migrations/` folder you commit alongside your code; `rask db update` runs it
against `app.db`. Every time you change an entity later, it's the same pair: `rask db add <Name>` then
`rask db update`.

## 4. Run it

```bash
rask dev
```

Browse to **`/products`**. You get a working list page with **New**, **Edit**, and **Delete** — each
button dispatching a real CQRS command that reads or writes SQLite. Create a product and refresh: it's
still there, because it's on disk in `app.db`.

## Verify

- `Features/Products/` exists with the entity, DbContext, and pages listed above.
- `Program.cs` has the three `AddRask…` / `AddDbContextFactory` lines and the app builds.
- After `rask db update`, an `app.db` file exists and `/products` renders.
- Creating a product then restarting the app still shows it (it's persisted, not in-memory).

> **Troubleshooting.** `rask generate` / `rask db` can't find the project → make sure you `cd`'d into
> `Shop` first. Build errors after pasting the DI block → you're missing a `using` (see step 2). `rask db
> update` fails with "no migrations" → you skipped `rask db add`.

**Learn more:** [data access](../data-access.md) · [Rask.Data](../data.md) · [CQRS](../cqrs.md) ·
[the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
