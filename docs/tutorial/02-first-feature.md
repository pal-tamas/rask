# Chapter 2 — Your first feature

> **Goal:** go from an empty app to a working, database-backed **Products** catalog — list, create, edit,
> delete — persisted in SQLite.
> **You'll run:** `rask generate feature Product …`, then `rask db add` / `rask db update`.

This chapter sets the pattern every later feature repeats — **generate → migrate**, with the CLI wiring the
services in for you in between. Do it once here and the rest of the tutorial is variations on it.

> **You don't name a database.** Chapter 1's `--all-batteries` already wired one — `AppDbContext` in
> `Features/Shared/`. The generator finds it and maps the new entity with it, so an app keeps **one**
> database and one set of migrations however many features you add. (Scaffolded a project without `--data`?
> Then there is no context yet, and this first run writes that same `Features/Shared/AppDbContext.cs` for
> you. Either way there's nothing to set up first — just generate.)

## 1. Generate the feature

From inside the `Shop` folder (the CLI finds the project by walking up from where you are, so `cd` in first):

```bash
rask generate feature Product Name:string Price:decimal InStock:bool \
  --validation dataannotations
```

Read the field list as `name:type`. Rask understands `string`, `int`, `long`, `decimal`, `double`, `bool`,
`datetime`, `date`, `time`, and `guid` — plus a few aliases (`text` → `string`, `money` → `decimal`). A
trailing `?` makes a field optional (`Description:string?`), and `string(100)` caps a string's length. `Id` is
added for you — don't list it.

> **`--validation` is optional; we pass `dataannotations`** here to keep the form validation familiar:
> `[Required]`, `[MaxLength]`, and a `DataAnnotationsValidator()` on the form. Drop the flag and you get
> Rask's default (`valueobjects`), which wraps each required string in a small DDD value-object type
> instead — nice for a real domain, more than a first feature needs. See [Rask.Data](../data.md) when you
> want it.
>
> **Quote fields that contain `?` or `()`** so your shell doesn't try to interpret them:
> `"Description:string?"`.

The generator writes a complete vertical slice into `Features/Products/`:

| File | What it is |
|------|-----------|
| `Product.cs` | the entity — an `Entity<Guid>` with a private constructor and `Create` / `Update` factory methods, so it can't be built in an invalid state |
| `ProductRequest.cs` | the form model the create/edit pages bind to (with the DataAnnotations) |
| `ProductConfiguration.cs` | the EF Core `IEntityTypeConfiguration<Product>` (column types, lengths) |
| `AppDbContext.cs` | *(already there from `--data`)* — gains a `DbSet<Product>` |
| `ProductsPage.cs` | the routed list page at `/products`, plus a `ListProductsQuery` + handler ([CQRS](../cqrs.md)) |
| `CreateProduct.cs`, `UpdateProduct.cs` | the new / edit pages, each with its own command + handler |
| `DeleteProduct.cs` | the delete command + a reusable delete button |

It also **adds the packages** the slice needs (EF Core + SQLite, `Rask.Cqrs`, `Rask.Data`) and restores.

Open `Features/Products/Product.cs` and `ProductsPage.cs` now — the generated code is the best documentation
of the patterns this tutorial uses.

## 2. The services are wired for you

The generator also **registers the services in `Program.cs`** — you'll see it report:

```
Registered 3 service(s) in Program.cs: AddRaskCqrs, AddRaskData, AddDbContextFactory<AppDbContext>.
```

so `Program.cs` now has these lines (and the `using`s they need) added next to your other
`builder.Services…` registrations:

```csharp
builder.Services.AddRaskCqrs();
builder.Services.AddRaskData();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

- `AddRaskCqrs()` registers the mediator that dispatches the queries/commands in the slice.
- `AddRaskData()` registers the interceptors (auditing, and later soft-delete/concurrency/events).
- `AddDbContextFactory<AppDbContext>(…)` registers the context **as a factory** — Rask pages are
  long-lived and may render concurrently, so each unit of work creates its own short-lived context rather
  than sharing one scoped instance. `UseRaskSqlite` is a drop-in for `UseSqlite` that also applies the
  production pragmas (WAL, `busy_timeout`, `foreign_keys`) — so the app handles concurrent writers (the jobs,
  email, and outbox you add in later chapters) without hitting `database is locked`. It defaults to a local
  `app.db` file next to the app but honours a `ConnectionStrings:App` override, which is how a deploy points
  it at a persistent volume.

The insert is idempotent, so generating a second feature only adds what's new. (If the CLI can't find or
safely edit your `Program.cs`, it prints the block instead for you to paste — same result.)

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
> `Shop` first. Saw "Couldn't find Program.cs" instead of the "Registered …" line → the CLI printed the
> registrations for you to paste (e.g. a non-standard host layout). `rask db update` fails with "no
> migrations" → you skipped `rask db add`.

**Learn more:** [data access](../data-access.md) · [Rask.Data](../data.md) · [CQRS](../cqrs.md) ·
[the `rask` CLI](../cli.md)

Next → **[Chapter 3: A second feature + locking it down](03-orders-and-auth.md)**
