# Cheat sheet

The one page to keep open. Every command, token, and wiring line you reach for while building — dense
and scannable. For the prose reference see [the `rask` CLI](cli.md); to learn it in order, follow the
[Tutorial](tutorial/00-overview.md). Looking for "how do I do X?" — that's the [Recipes](recipes.md).

## The CLI in a screenful

```bash
# scaffold & run
rask new Shop                        # new app: the whole stack, accounts included
rask dev                              # dotnet watch run — hot reload (--open for a browser)
rask info                             # what rask sees: project, packages, versions

# database (wraps dotnet-ef; installs it on first use)
rask db add InitialCreate             # generate a migration from the current model
rask db update                        # apply it — creates app.db
rask db list                          # which migrations exist, and which are applied
rask db remove                        # undo the last migration
rask db drop --yes                  # delete the database, no prompt
rask db backup                        # copy it to ./<app>-<timestamp>.db
rask db restore backups/shop.db       # put a copy back (--remote for the deployed one)

# ship it
rask deploy --host root@box --domain shop.example.com   # bare box → live HTTPS
rask deploy                           # redeploy (host/domain remembered), zero-downtime
rask deploy --github-actions          # write .github/workflows/deploy.yml
rask deploy status                    # what's running, and on which color
rask deploy logs -f                  # tail the deployed app (--follow)
rask deploy rollback                  # put the previous image back

# tab completion
rask completion zsh > "${fpath[1]}/_rask"   # also: bash, fish
```

Every command's own option list is in `rask <command> --help`, and a wrong one tells you what you
probably meant.

> **Code inside the project is code you write.** `rask` scaffolds the project; pages, components, CRUD
> slices, jobs, emails and caches are shown as copyable code in the [tutorial](tutorial/00-overview.md)
> and the guides, and the finished app is committed as
> [`samples/Rask.Example.Shop`](https://github.com/pal-tamas/rask/tree/main/samples/Rask.Example.Shop).

## A CRUD slice, in one place

A vertical slice under `Features/<Plural>/` is five kinds of file:

| File | What it is |
|---|---|
| `Product.cs` | the entity — `Entity<Guid>`, private setters, `Create`/`Update` factories |
| `ProductRequest.cs` | the form model the create/edit pages bind to |
| `ProductConfiguration.cs` | the EF `IEntityTypeConfiguration<Product>` (lengths, keys, relationships) |
| `ProductsPage.cs` | the routed list page + its query and handler |
| `CreateProduct.cs` · `UpdateProduct.cs` · `DeleteProduct.cs` | one command + handler + page each |

Plus `public DbSet<Product> Products => Set<Product>();` on the app's one `AppDbContext`.
[Chapter 2](tutorial/02-first-feature.md) writes all of it out.

## Wiring one-liners

A data-backed app needs these three in `Program.cs` (`rask new` writes them for you):

```csharp
builder.Services.AddRaskCqrs();                        // the mediator (IDispatcher)
builder.Services.AddRaskData();                        // EF interceptors: audit/soft-delete/events
builder.Services.AddDbContextFactory<ProductsDbContext>((sp, o) => o
    .UseSqlite("Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));   // audit/soft-delete/events/outbox
```

The other pillars are **one registration + one `modelBuilder` line + a migration** you add by hand:

```csharp
builder.Services.AddRaskJobs<ProductsDbContext>(o => { /* … */ });   modelBuilder.AddRaskJobs();
builder.Services.AddRaskMail<ProductsDbContext>(o => { /* … */ });   modelBuilder.AddRaskMail();
builder.Services.AddRaskCache<ProductsDbContext>();                  modelBuilder.AddRaskCache();
builder.Services.AddRaskOutbox<ProductsDbContext>(o => { /* … */ }); modelBuilder.AddRaskOutbox();
// the outbox claims domain-event delivery on its own — AddRaskData stays bare, in any order

// production SQLite — a drop-in for .UseSqlite that installs the pragma interceptor:
.UseRaskSqlite("Data Source=app.db")
builder.Services.AddRaskSqliteLitestream(o => { /* off-box backup */ });
```

After any `modelBuilder.AddRask…` line: `rask db add <Name>` → `rask db update`.

## Code idioms

```csharp
// Dispatch a query or command — one method, result type inferred from the message:
var view = await dispatcher.QueryAsync(new GetProducts(), CancellationToken);   // IDispatcher, ctor-injected

// Type-safe URL for a routed page — never a string path:
NavLink.Href(Routes.ProductsPage())["Catalog"];       // list page → <Plural>Page
nav.NavigateTo(Routes.UpdateProduct(Id: id));          // edit page → Update<Entity>; Navigator, event-handler only

// Gate on auth — route-level attribute, or a component that renders only when signed in:
[Authorize]                                            // redirects anonymous deep-links to /login
Authorize[ NewProductButton() ]                      // shown only to signed-in users
Authorize.Roles(["admin"])[ DeleteProductButton(id) ]

// Cache an expensive read; invalidate on write:
var products = await cache.GetOrAddAsync("products", async _ => await LoadAsync(), CancellationToken);
await cache.RemoveAsync("products");

// Enqueue work off the request thread — returns as soon as the row is written:
await jobs.EnqueueAsync(new SendOrderReceipt(order.Id), CancellationToken);
```

---

Full reference → [the `rask` CLI](cli.md) · Learn it in order → [Tutorial](tutorial/00-overview.md) ·
How do I…? → [Recipes](recipes.md)
