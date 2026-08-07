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
| stop retyping the same feature flags | [↓](#save-team-defaults) |
| get tests with a generated feature | [↓](#generate-tests-for-a-feature) |

---

## Add a feature to an existing database

Nothing to pass: a feature attaches to the `DbContext` the project already has — the CLI finds the class,
adds the `DbSet`, and wires the cross-namespace `using` so it compiles. Name one with `--context` only when
the project has several and the CLI asks which.

```bash
rask g f Order Total:decimal ProductId:guid Placed:datetime
rask db add AddOrder && rask db update
```

→ Reference: [data access](data.md) · Learn it: [Tutorial Ch 3](tutorial/03-orders-and-auth.md)

## Add a related entity

Name a cardinality, a target entity, and its fields **after the root's fields** — both slices are
generated in one run, with the foreign key, navigation properties, and EF mapping in place.

```bash
rask g f Post Title:string 1:n Comment Body:text     # Post → many Comments (Comment.PostId + Comment.Post, Post.Comments)
```

Cardinalities: `1:n` `n:1` `1:1` `n:n` — a leading `0` (`0:n`) makes the foreign key optional; `n:n`
maps through EF Core's implicit join table (no join entity).

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

Generate a job, add one registration + its table, then enqueue. `EnqueueAsync` returns as soon as the
row is written, so the request finishes immediately; a background processor runs it at-least-once.

```bash
rask g job SendOrderReceipt
```
```csharp
builder.Services.AddRaskJobs<ProductsDbContext>(o => { /* … */ });   // needs AddRaskCqrs()
modelBuilder.AddRaskJobs();                                          // then: rask db add AddJobs && rask db update
await jobs.EnqueueAsync(new SendOrderReceipt(order.Id), CancellationToken);
```

→ Reference: [background jobs](jobs.md) · Learn it: [Tutorial Ch 4](tutorial/04-background-jobs.md)

## Send a transactional email

Generate an email whose body is a Rask component, add the mail queue, then send. Delivery happens off
the request thread over SMTP with backoff.

```bash
rask g email OrderReceipt
```
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
post-commit (crash-safe, at-least-once). The fastest path is to scaffold it with the feature:

```bash
rask g f Order Total:decimal --outbox        # emits the event records, the Raise calls, and a handler stub
```

For an existing feature, wire it by hand:

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

## Save team defaults

So a project stops retyping the same feature flags, record them once in `.rask/generate.json`; explicit
flags on the command line always win.

```bash
rask g f Order Total:decimal --bs --tests --save-defaults    # scaffolds and remembers --bs/--tests
```

→ Reference: [the `rask` CLI](cli.md#rask-generate--scaffold-code)

## Generate tests for a feature

`--tests` emits a sibling `<Project>.Tests` project (created and wired on first use) with a domain test
and — when the `DbContext` is generated — a SQLite round-trip persistence test, so `dotnet test` runs
as-is.

```bash
rask g f Product Name:string Price:decimal --tests
```

→ Reference: [testing](testing.md)

---

Command reference → [the `rask` CLI](cli.md) · One-page reference → [Cheat sheet](cheatsheet.md) ·
Learn it in order → [Tutorial](tutorial/00-overview.md)
