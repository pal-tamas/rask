# Recipes

Task-first answers to "how do I do X in an app I already have?" Each recipe is the shortest path —
the command, the one wiring line, and where to go deeper. The [Tutorial](tutorial/00-overview.md)
teaches these in order on one app; this page is the lookup. Keep the [Cheat sheet](cheatsheet.md)
open alongside.

| I want to… | Jump to |
|---|---|
| add a CRUD feature to a database I already have | [↓](#add-a-feature-to-an-existing-database) |
| relate two entities (one-to-many, many-to-many) | [↓](#add-a-related-entity) |
| keep deleted rows, or forbid deleting | [↓](#keep-deleted-rows-or-forbid-deleting) |
| add a search box over a table | [↓](#add-a-search-box) |
| know who is signed in, from anywhere | [↓](#know-who-is-signed-in) |
| keep each customer's data apart | [↓](#keep-each-customers-data-apart) |
| cache a query for the page, refreshed after writes | [↓](#cache-a-query-for-the-page) |
| require a login to reach a page | [↓](#require-login-on-a-page) |
| run work off the request thread | [↓](#run-work-off-the-request-thread) |
| send a transactional email | [↓](#send-a-transactional-email) |
| cache an expensive query | [↓](#cache-an-expensive-query) |
| keep a file a user uploads | [↓](#keep-an-uploaded-file) |
| publish a domain event durably | [↓](#publish-a-domain-event-through-the-outbox) |
| harden SQLite for production | [↓](#turn-on-production-sqlite) |
| deploy, and redeploy | [↓](#deploy-and-redeploy) |
| test a feature | [↓](#test-a-feature) |

---

## Add a feature to an existing database

Declare the aggregate under `Features/Orders/` and its pages beside it. Nothing goes on the context — no
`DbSet`, no configuration class: `RaskDbContext` maps every aggregate you declare, and the build generates
its form model (`OrderModel`), read face (`Order.Read`) and writes (`Order.Create(model)`).

```csharp
public sealed class Order : Aggregate<Guid>
{
    [Required, MaxLength(40)]
    public string Reference { get; private set; } = "";
}
```

```bash
rask db add AddOrder && rask db update
```

→ Reference: [Rask.Data](data.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Add a related entity

**Another aggregate** is referred to by its id, never held — a navigation from one aggregate to another is a
build error ([RASK087](diagnostics.md#rask087)). The read face infers the join from the id's name:

```csharp
public Guid CustomerId { get; private set; }            // on Order

var mine = await Order.Read.Where(o => o.Customer.Country == "HU").ToListAsync();   // navigation, inferred
```

**A part of the aggregate** — an order's lines — is an `Entity<TId>` kept in a private list and changed only
through the root; loading the root by id loads it whole. Many-to-many between aggregates is a list of ids.

→ Reference: [children](data.md#children) · [the read face](data.md#what-the-read-face-is) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Keep deleted rows, or forbid deleting

A delete removes the row. Declare a `const` on the aggregate to say otherwise:

```csharp
public const Deletion Deletes = Deletion.Soft;   // Order.Delete stamps DeletedAt; reads hide the row
public const Deletion Deletes = Deletion.None;   // no Order.Delete at all — cancel it instead
```

Then `rask db add OrderDeletes && rask db update`. `Order.Read.IgnoreQueryFilters()` brings soft-deleted rows
back into a read.

→ Reference: [choosing what a table carries](data.md#choosing-what-a-table-carries)

## Add a search box

Declare which text is searchable in the aggregate's `static Configure`, add a migration, and search the read
face — ranked, word-aware and accent-insensitive, on SQLite, in the browser and on PostgreSQL:

```csharp
public static void Configure(EntityTypeBuilder<Product> builder) =>
    builder.HasFullTextSearch(p => new { p.Name, p.Description });   // then: rask db add ProductSearch && rask db update

var hits = await Product.Read.Search(query).Take(20).ToListAsync(CancellationToken);
Ui.DataGrid.Data(Product.Read.Search(query).AsQueryable())           // best match first, paged in SQL
```

→ Reference: [full-text search](full-text-search.md)

## Know who is signed in

`Current` answers from anywhere — a static factory, a command handler, an API endpoint, a job — with nothing
injected:

```csharp
public static Note Create(string text) => new() { Id = Guid.CreateVersion7(), Text = text, OwnerId = Current.RequiredUserId };
```

`Current.UserId` is `null` when nobody is signed in, and `Current.RequiredUserId` throws. A component that
must re-render on sign-in injects `IUserProvider` instead.

→ Reference: [`Current`](data.md#the-current-user--current)

## Keep each customer's data apart

One `const` partitions a table by tenant; every read filters to the signed-in user's tenant and every create
stamps it:

```csharp
public sealed class Invoice : Aggregate<Guid>
{
    public const Tenancy Scope = Tenancy.PerTenant;
}

using (Tenant.Use(tenantId)) { /* an admin working in one tenant */ }
```

→ Reference: [multi-tenancy](multi-tenancy.md)

## Cache a query for the page

`QueryClient.Query` caches a read for the session, deduplicates it and refetches it in the background. Keyed
by the aggregate, it refreshes itself after any write to that aggregate — no invalidation to write:

```csharp
var count = QueryClient.Query(QueryKey.For<Product>("count"), ct => Product.Read.CountAsync(ct));
```

→ Reference: [Rask.Query](query.md) · Learn it: [Tutorial Ch 2](tutorial/02-first-feature.md)

## Require login on a page

Two gates, use both: `[Authorize]` at the route (redirects anonymous deep-links to `/login`), and the
`Authorize` component to hide UI that anonymous users shouldn't see.

```csharp
[Authorize]                                    // route-level; from Microsoft.AspNetCore.Authorization
public sealed partial class CreateProduct : Component { … }

Authorize[ NewProductButton() ]              // rendered only for signed-in users
Authorize.Roles(["admin"])[ DeleteProductButton(product.Id) ]
```

The login page itself is already there: `/login`, `/register` and `/logout` are built in, and you
replace any of them by declaring your own page at the same route.

→ Reference: [authentication](authentication.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Run work off the request thread

Write a job record and handler, add one registration + its table, then enqueue. The enqueue returns as
soon as the row is written, so the request finishes immediately; a background processor runs it
at-least-once.

```csharp
public sealed record SendOrderReceipt(Guid OrderId) : IJob;
```
```csharp
builder.Services.AddRaskJobs<ProductsDbContext>(o => { /* … */ });   // needs AddRaskCqrs()
modelBuilder.AddRaskJobs();                                          // then: rask db add AddJobs && rask db update
await Jobs.Enqueue(new SendOrderReceipt(order.Id));                  // or: .In(24.Hours)
```

→ Reference: [background jobs](jobs.md) · Learn it: [Tutorial Ch 4](tutorial/04-background-jobs.md)

## Send a transactional email

Write an email whose body is a Rask component, add the mail queue, then send. Delivery happens off
the request thread over SMTP with backoff.

```csharp
builder.Services.AddRaskMail<ProductsDbContext>(o => { /* SMTP … */ });
modelBuilder.AddRaskMail();                                          // then: rask db add AddMail && rask db update
```

→ Reference: [transactional email](mail.md) · Learn it: [Tutorial Ch 5](tutorial/05-email.md)

## Cache an expensive query

One registration + one table, then remember the read and forget it on write.

```csharp
builder.Services.AddRaskCache<ProductsDbContext>();
modelBuilder.AddRaskCache();                                         // then: rask db add AddCache && rask db update

var products = await Cache.Remember("products", LoadProducts).For(10.Minutes);
await Cache.Forget("products");                                      // when the catalog changes
```

→ Reference: [cache](cache.md) · Learn it: [Tutorial Ch 6](tutorial/06-cache.md)

## Keep an uploaded file

Save the `RaskFile` from the picker's handler with `Files.Save`, keep the returned id on your entity, and link
to it. A `RaskApp` already has storage on; a hand-wired host adds the registration, the table and the routes.

```csharp
builder.Services.AddRaskStorage<ProductsDbContext>();
modelBuilder.AddRaskStorage();                                       // then: rask db add AddStorage && rask db update
app.MapRaskStorage();                                                // after app.MapRask<App>()

var saved = await Files.Save(file.OpenReadStream, file.Name, file.Size).Public();
product.SetPhoto(saved.Id);
Img.Src(Files.Url(saved.Id)).Alt(product.Name)                       // no I/O — safe inside Render
```

Files on the default disk provider are archived by `rask db backup` beside the database, but not by Litestream
or snapshots; use S3 or Azure for uploads you can't afford to lose.

→ Reference: [file storage](file-storage.md) · The picker: [HTTP & files](http-and-files.md#uploading-files)

## Publish a domain event through the outbox

Events are written to an `OutboxMessage` row **in the same transaction** as your data, then delivered
post-commit (crash-safe, at-least-once). Declare the events, raise them from the entity, and wire the
outbox:

```csharp
public sealed record OrderCreated(Guid Id) : IOutboxEvent;   // then Raise(new OrderCreated(Id)) in Create
```
```csharp
builder.Services.AddRaskData();                                     // unchanged — the outbox claims delivery
builder.Services.AddRaskOutbox<ProductsDbContext>(o => { /* … */ });
modelBuilder.AddRaskOutbox();                                        // then: rask db add AddOutbox && rask db update
```

→ Reference: [outbox](outbox.md) · Learn it: [Tutorial Ch 7](tutorial/07-outbox-events.md)

## Turn on production SQLite

`UseRaskSqlite` is a drop-in for `.UseSqlite` that installs the pragma interceptor (WAL, `foreign_keys`,
`busy_timeout` on every open). Add Litestream for continuous off-box backup.

```csharp
.UseRaskSqlite(sp)                         // was .UseSqlite("Data Source=app.db"); reads Rask:ConnectionStrings:App
builder.Services.AddRaskSqliteLitestream();   // reads Rask:Litestream — set ReplicaUrl to turn it on
```

→ Reference: [production SQLite](sqlite.md) · Learn it: [Tutorial Ch 8](tutorial/08-production-sqlite.md)

## Deploy and redeploy

`rask deploy` builds your Docker image on the server, runs it behind a shared Caddy proxy with
automatic HTTPS, and does a health-gated zero-downtime swap. The first run on a bare box also sets it
up (Docker, a non-root deploy user, firewall, SSH hardening).

```bash
rask deploy --host root@your-box.example.com --domain shop.example.com   # first time
rask deploy                                                             # after: host/domain remembered
rask deploy --github-actions                                            # write .github/workflows/deploy.yml
```

Needs a `Dockerfile` — `rask new` writes one.

→ Reference: [deployment](deployment.md) · Learn it: [Tutorial Ch 11](tutorial/11-deploy.md)

## Test a feature

Add a sibling `<Project>.Tests` project and test the slice directly — the domain rules on the entity, and
a SQLite round-trip through the real `DbContext`:

```csharp
[Fact]
public void Create_sets_the_fields()
{
    var product = Product.Create("Desk", 249m, inStock: true);

    Assert.Equal("Desk", product.Name);
    Assert.NotEqual(Guid.Empty, product.Id);
}
```

→ Reference: [testing](testing.md)

---

Command reference → [the `rask` CLI](cli.md) · One-page reference → [Cheat sheet](cheatsheet.md) ·
Learn it in order → [Tutorial](tutorial/00-overview.md)
