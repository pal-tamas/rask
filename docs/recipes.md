# Recipes

Task-first answers to "how do I do X in an app I already have?" Each recipe is the shortest path —
the command, the one wiring line, and where to go deeper. The [Tutorial](tutorial/00-overview.md)
teaches these in order on one app; this page is the lookup. Keep the [Cheat sheet](cheatsheet.md)
open alongside.

| I want to… | Jump to |
|---|---|
| add a CRUD feature to a database I already have | [↓](#add-a-feature-to-an-existing-database) |
| relate two entities (one-to-many, many-to-many) | [↓](#add-a-related-entity) |
| require a login to reach a page | [↓](#require-login-on-a-page) |
| run work off the request thread | [↓](#run-work-off-the-request-thread) |
| send a transactional email | [↓](#send-a-transactional-email) |
| cache an expensive query | [↓](#cache-an-expensive-query) |
| publish a domain event durably | [↓](#publish-a-domain-event-through-the-outbox) |
| harden SQLite for production | [↓](#turn-on-production-sqlite) |
| deploy, and redeploy | [↓](#deploy-and-redeploy) |
| test a feature | [↓](#test-a-feature) |

---

## Add a feature to an existing database

A feature maps through the `DbContext` the project already has — add the entity, its configuration and its
pages under `Features/Orders/`, then one line to the context:

```csharp
public DbSet<Order> Orders => Set<Order>();
```

```bash
rask db add AddOrder && rask db update
```

→ Reference: [data access](data.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Add a related entity

Give the child a foreign key and the parent a collection, then map it in the child's
`IEntityTypeConfiguration`:

```csharp
// Comment.cs
public Guid PostId { get; private set; }

// CommentConfiguration.cs
entity.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId);
```

Use `.IsRequired(false)` on the foreign key for an optional relationship, and EF Core's implicit join
table for many-to-many (no join entity needed).

→ Reference: [the `rask` CLI](cli.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Require login on a page

Two gates, use both: `[Authorize]` at the route (redirects anonymous deep-links to `/login`), and the
`Authorize` component to hide UI that anonymous users shouldn't see.

```csharp
[Authorize]                                    // route-level; from Microsoft.AspNetCore.Authorization
public sealed class CreateProduct : Component { … }

Authorize()[ NewProductButton() ]              // rendered only for signed-in users
Authorize(Roles: ["admin"])[ DeleteProductButton(product.Id) ]
```

Scaffold the login itself with `rask new … --auth`.

→ Reference: [authentication](authentication.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Run work off the request thread

Write a job record and handler, add one registration + its table, then enqueue. `EnqueueAsync` returns as
soon as the row is written, so the request finishes immediately; a background processor runs it
at-least-once.

```csharp
public sealed record SendOrderReceipt(Guid OrderId) : IJob;
```
```csharp
builder.Services.AddRaskJobs<ProductsDbContext>(o => { /* … */ });   // needs AddRaskCqrs()
modelBuilder.AddRaskJobs();                                          // then: rask db add AddJobs && rask db update
await jobs.EnqueueAsync(new SendOrderReceipt(order.Id), CancellationToken);
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

One registration + one table, then wrap the read in `GetOrCreateAsync` and invalidate on write.

```csharp
builder.Services.AddRaskCache<ProductsDbContext>();
modelBuilder.AddRaskCache();                                         // then: rask db add AddCache && rask db update

var products = await cache.GetOrCreateAsync("products", async _ => await LoadAsync(), CancellationToken);
await cache.RemoveAsync("products");                                 // when the catalog changes
```

→ Reference: [cache](cache.md) · Learn it: [Tutorial Ch 6](tutorial/06-cache.md)

## Publish a domain event through the outbox

Events are written to an `OutboxMessage` row **in the same transaction** as your data, then delivered
post-commit (crash-safe, at-least-once). Declare the events, raise them from the entity, and wire the
outbox:

```csharp
public sealed record OrderCreated(Guid Id) : IOutboxEvent;   // then Raise(new OrderCreated(Id)) in Create
```
```csharp
builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);   // was AddRaskData()
builder.Services.AddRaskOutbox<ProductsDbContext>(o => { /* … */ });
modelBuilder.AddRaskOutbox();                                        // then: rask db add AddOutbox && rask db update
```

→ Reference: [outbox](outbox.md) · Learn it: [Tutorial Ch 7](tutorial/07-outbox-events.md)

## Turn on production SQLite

`UseRaskSqlite` is a drop-in for `.UseSqlite` that installs the pragma interceptor (WAL, `foreign_keys`,
`busy_timeout` on every open). Add Litestream for continuous off-box backup.

```csharp
.UseRaskSqlite("Data Source=app.db")                                // was .UseSqlite("Data Source=app.db")
builder.Services.AddRaskSqliteLitestream(o => { /* S3/replica … */ });
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

Needs a `Dockerfile` — `rask new … --docker` writes one.

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
