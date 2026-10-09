# Multi-tenancy — one database, each customer's rows kept apart

> **In practice:** [Rask.Data](data.md) · [authentication](authentication.md#accounts-and-tenants) · [cheat sheet](cheatsheet.md).

A product sold to companies usually keeps every company's data in one database and makes sure no company
ever sees another's rows. Each company is a **tenant**. Getting that right by hand means a `Where(x =>
x.TenantId == …)` on every query, a stamp on every insert and a tenant column at the front of every unique
index, and one forgotten predicate is a data leak. Rask does all three from one declaration on the table.

It is **opt-in**. A table that says nothing is one table for everybody, and an app with no tenant-scoped
table has nothing to configure.

## Declaring a tenant-scoped table

One `const` partitions a table by tenant — the fifth member of the family that
[chooses what a table carries](data.md#choosing-what-a-table-carries):

```csharp
public sealed class Invoice : Aggregate<Guid>
{
    public const Tenancy Scope = Tenancy.PerTenant;

    public string Reference { get; private set; } = "";
}
```

That gives the table a `TenantId` column, a query filter no read can compose away, and a `TenantId` prefix on
every index it has. The default, `Tenancy.Shared`, is what a table that declares nothing gets. A child entity
takes its root's answer and carries the column itself, because a child's read face is queryable on its own and
would otherwise return every tenant's rows.

The column is the framework's, so the entity does not mention it. One that wants to read its tenant declares
the property, and Rask maps the column to it:

```csharp
public sealed class Invoice : Aggregate<Guid>
{
    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }
}
```

**Why a `const`.** Like the other four, it is read at compile time rather than reflected over, so a trimmed
publish cannot lose it and quietly fall back to "shared".

### Tenants that are numbered

An app whose tenants are rows with an integer key declares the column in that type, and the declared type is
the column's, the filter's and the stamp's:

```csharp
public sealed class Destination : Aggregate<int>
{
    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }     // or long?

    public string Name { get; private set; } = "";
}

using (Tenant.Use(42))
{
    await Destination.Named("Budapest").Save();     // TenantId = 42
    var mine = await Destination.OrderBy(d => d.Name);   // WHERE TenantId = 42
}
```

`Guid?`, `int?` and `long?` are the three types a tenant is kept in — nullable, because a row has none until
it is saved — and any other is refused when the model is built. An entity that declares nothing keeps a shadow
`Guid?`, a child included: a child of a numbered aggregate declares its own `int? TenantId` to keep the number.

Everywhere else the tenant is still a `Guid` — `Current.Tenant`, a job's row, a cache key — and a number
travels inside it by a fixed rule: **the first eight bytes are zero and the last eight are the number,
big-endian**. Tenant 42 is `00000000-0000-0000-0000-00000000002a`, readable by eye on a job row or in a log, and
it is what lets a job enqueued in tenant 42 run in tenant 42. No generated `Guid` has those eight zero bytes,
so a real identifier is never taken for a number. Two consequences:

- A tenant that is **not** a number — a `Guid` from a claim — met by a table that keeps a number is refused
  with an exception, on a read as on a write, rather than compared with something. So is a number too large
  for an `int` column, which would otherwise truncate into another tenant's.
- Tenant `0` is `Guid.Empty`, which the batteries read as "no tenant". Number tenants from one.

## Where the tenant comes from

**The signed-in user's.** The tenant is an outcome of authentication rather than an input to routing: signing
in finds the user, and the user says which tenant they belong to. It travels on the `ClaimsPrincipal` as the
`rask:tenant` claim (`Tenant.ClaimType`), and the data layer reads it back — which is what lets a page do this
with nothing passed to it:

```csharp
var invoices = await Invoice.OrderByDescending(i => i.CreatedAt).Take(20);
```

A restored session carries the claim too, so a reconnect comes back in the same tenant. The same holds for
every HTTP request — a minimal API, a controller, a CQRS endpoint — because the host makes the request's
services ambient for the data layer after authentication has run.

`Current.Tenant` says which tenant that is, from anywhere, and `Current.RequiredTenant` throws when there is
none. The order it answers in is: an explicit `Tenant.Use` scope first, then the signed-in user's claim — or,
in an app that registered one, [its resolver](#from-the-request-a-resolver) in the claim's place — and `null`
inside `Tenant.Across()`. See [`Current`](data.md#the-current-user--current) for the user it answers
alongside.

### Putting a user in a tenant

Rask.Auth records the tenant on the account row but does not decide it — your app does, usually when an
invitation is accepted or a company signs up. `Authenticatable` has a protected `RecordTenant`, so your `User`
can say what joining a tenant means, and the registration lambda sets it in the same insert:

```csharp
public sealed class User : Authenticatable
{
    public void JoinTenant(Guid tenant) => RecordTenant(tenant);
}

await auth.Register(model.Email, model.Password, (User user) => user.JoinTenant(invite.TenantId), ReturnUrl);
```

A user with no tenant — an administrator — carries no claim, and is covered [below](#administrators).

### From the request: a resolver

An app that knows its tenant some other way — a host name per customer, a header, a route value, a request
service it already has — says so once, and that replaces the claim as the source:

```csharp
builder.Services.AddRaskTenant(sp => sp.GetRequiredService<ICurrentRequest>().TenantId);   // int?, long? or Guid?
```

The function is handed the services of the work in flight and returns its tenant, or `null` when it belongs to
none. It is asked **once per scope and the answer is kept**: as an HTTP request starts, and as a live session
opens — for as long as that session lives, so a session does not change tenant because a later message
arrived without the request that opened it. `Current.Tenant`, every filter and every stamp then use it.

| | With a resolver registered |
|---|---|
| `Tenant.Use(id)` / `Tenant.Across()` | still win. A background job goes on running in the tenant its row recorded, and there the resolver is not even asked |
| a signed-in user with no tenant claim | works in the resolved tenant — an administrator on a customer's host, or anybody in an app that signs people in itself |
| a signed-in user whose claim names **another** tenant | refused: reading the tenant throws `ForbiddenException` |
| the resolver returns `null` | [no tenant](#when-the-resolver-names-no-tenant): reads are empty, writes are refused |

**What `sp` is.** Under a Rask host — `RaskApp`, or a host wired by hand with
[`AddRaskData<TContext>(o => …)` and `app.UseRaskData()`](data.md#a-second-context-beside-the-one-you-have):

| The work | `sp` | When it is asked |
|---|---|---|
| an HTTP request — an endpoint, a controller, the GET that renders a page | that request's `RequestServices` | as the request reaches `UseRaskData()`, after authentication |
| a live session — a page's handlers, its re-renders | the **session's** own scope, which lives as long as the page is open | once, on the session's first render, which runs inside the request that opened the page; never again |

So a resolver that goes through a **singleton** reading `IHttpContextAccessor` — the shape most apps already
have for "which customer is this request for" — works for both: when the session is asked, the request that
opened it is still in flight, and what it answers then is kept for every later message on the socket, whatever
that socket's own request looks like. The signed-in user is there too, in both: `Current.Principal` is
`HttpContext.User` for a request and the user the session was opened by for a session.

Three things to know when writing one:

- **A live session's scope is not a request's.** A *scoped* object a middleware filled in for the request is a
  different, empty instance in the session's scope — read the request through `IHttpContextAccessor`, or
  resolve a singleton that does.
- **It also runs for background work that recorded no tenant** — a recurring job the host scheduled — where
  there is no request. Return `null` then; do not assume one.
- **It must not read the tenant it is being asked for.** Resolve it from the request, or from a table that is
  not tenant-scoped (the table of tenants itself); a resolver that reads `Current.Tenant` is told so.

`AddRaskTenant` is called once; a second registration is refused at startup.

#### When the resolver names no tenant

```csharp
await Invoice.Count();                    // 0
await Invoice.Find(id);                   // null
await Invoice.For("A-1").Save();          // MissingTenantException
```

A request on a host no customer owns is an ordinary thing in an app that resolves its tenant from the request,
so it is not an error to read there: a tenant-scoped table is **empty**. It is never every tenant's rows, and
never the rows that belong to no tenant either. Every write to one — create, update, delete — is refused with
a `MissingTenantException` (an `InvalidOperationException`), because a row saved for nobody is a row nobody
can read. This is the one place the rule [below](#a-read-with-no-tenant-throws) gives way, and only for an app
that registered a resolver.

## Saying which tenant explicitly

```csharp
using (Tenant.Use(acmeId))          // work as this tenant
{
    await Invoice.Create(model);
}

using (Tenant.Across())             // deliberately span tenants — an admin tool, a migration
{
    var total = await Invoice.Count();
}
```

An explicit scope always beats the principal, which is what a background job relies on. `Tenant.None()` clears
the tenant for its scope — for a test, or for code that has to find a user before it can know their tenant.

## Administrators

An administrator belongs to no tenant, so they carry no tenant claim, and a tenant-scoped read throws until
they say which tenant they are acting in. That is intended: an admin should choose a tenant, not silently read
across all of them. An admin page keeps the tenant the admin picked and opens a scope around the work:

```csharp
private Guid _tenant;   // chosen from a list of tenants

private async Task Load()
{
    using (Tenant.Use(_tenant))
    {
        _invoices = await Invoice.OrderByDescending(i => i.CreatedAt);
    }
}
```

A report over every tenant is `Tenant.Across()` — one greppable thing a reviewer can find.

## A read with no tenant throws

```csharp
await Invoice.All;   // InvalidOperationException, when nothing says which tenant
```

Deliberately, and it is the decision most worth understanding. Returning *nothing* would be safe against
leaks and **indistinguishable from an empty database** — the failure that costs the most time to find.
Returning *everything* would be the leak itself. So it refuses, and says how to say which tenant.

An app that registered a [resolver](#from-the-request-a-resolver) has told Rask that "no tenant" is an answer
it gives on purpose, and there the read is empty instead — see
[When the resolver names no tenant](#when-the-resolver-names-no-tenant).

## `IgnoreQueryFilters()` does not cross tenants

```csharp
await Invoice.IgnoreQueryFilters();   // includes soft-deleted; SAME tenant
```

It means "include soft-deleted rows" and leaves the tenant filter exactly where it is, so an existing call
never quietly becomes a cross-tenant read the day an aggregate declares `Scope`. Crossing tenants is
`Tenant.Across()`. This uses EF Core 10's named query filters.

## Indexes are prefixed for you

`HasIndex(p => p.Sku).IsUnique()` on a partitioned table would otherwise mean "no two tenants may ever use
the same SKU", and the symptom is one tenant unable to create a row because a different tenant already has
it, with nothing in the code saying so. Rask puts `TenantId` at the front of every index on a tenant-scoped
entity that does not name the tenant, so uniqueness means *within this tenant* and the filtered query can use
the index — wherever the index was declared: in the entity's `Configure`, by an `[Index]` attribute, or in a
context of your own.

**An index that already names `TenantId` is left exactly as written**, wherever in the index it is:

```csharp
public static void Configure(EntityTypeBuilder<Destination> builder)
{
    builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique();   // stays (Name, TenantId)
    builder.HasIndex(d => d.Code).IsUnique();                       // becomes (TenantId, Code)
}
```

That is what lets a table that already exists keep the indexes it has. A declared `TenantId` is what the first
line needs; for a shadow one, name it as a string — `builder.HasIndex("Name", Columns.TenantId)`.

## Writes stamp it, and it never moves

A create records the tenant in flight — the signed-in user's, with no `Tenant.Use` needed. An update that
would move a row to another tenant is refused: copy the row into the other tenant instead. The column is never
on the generated form model, declared or not, so a post cannot set it. Inserting a tenant-scoped row inside `Tenant.Across()`
is refused too, unless the row's `TenantId` is already set, because "across" names no tenant to stamp.

## The batteries

Rask's own tables carry a tenant without being partitioned by one. A partitioned table takes a query filter,
and each of these has a background runner that must see every tenant's rows — so the tenant is **data** on
the row, recorded when the work is created and honoured when it is used.

| Battery | What tenancy means there |
|---|---|
| [Jobs](jobs.md), [mail](mail.md), [outbox](outbox.md) | Each row records the tenant in flight when it is enqueued, and the runner re-enters it before handling, sending or publishing — so a handler that reads a tenant-scoped table works as it did on the page. A job records the user too, so `Current.UserId` answers in its handler. |
| [Cache](cache.md) | One cache, isolated by the key: with a tenant in flight the key is scoped to it, so each tenant gets its own value under the same name and cannot form another's key. With no tenant the key is untouched, which keeps anonymous requests — ASP.NET session and output caching — working. |
| [File storage](file-storage.md) | A file belongs to the tenant that saved it. Looking one up, opening it, handing out a temporary URL and deleting it are scoped to the tenant in flight, so a tenant holding another's file id still cannot reach it. The orphan sweep sees every tenant's files. |
| [Accounts](authentication.md#accounts-and-tenants) | An address is unique within a tenant, so one person can hold an account at two companies; sign-in refuses an address that two tenants hold rather than guessing. |

A row created by the host itself — a recurring job scheduled at startup — or inside `Tenant.Across()` belongs
to no tenant, which is an ordinary answer rather than an error.

## What it needs from the host

The context passes itself to the conventions, and says it can answer which tenant:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyRaskConventions(this);   // `this`, so the filter can read the tenant
    }
}
```

`rask new` writes that, and `RaskDbContext` implements `ITenantScoped`; a context over plain `DbContext`
declares `: DbContext, ITenantScoped` itself. The argument is not decoration: a query filter is compiled into
EF Core's **cached** model, so reading the tenant through a `static` is evaluated once and inlined into the SQL
as a literal — the first tenant to run a query would pin that value for every tenant after it. Reaching it
through the context makes EF lift it to a real parameter and re-bind it per query. The parameterless
`ApplyRaskConventions()` refuses a model with a tenant-scoped table rather than build a filter it cannot.

The auditing interceptors must be registered, as they already must be for `CreatedAt`. Without them nothing
stamps `TenantId`, every insert stores null, and the rows are invisible to every tenant — an empty result
rather than an error.

Outside a Rask app, the host also has to say who is signed in: register an `IPrincipalSource` (scoped) that
returns the session's or request's principal, and open `Db.UseScope(scope)` around each request's work so a
static read can reach it. See [Wiring, when Rask is not hosting](data.md#wiring-when-rask-is-not-hosting). The
same scope is where a [resolver](#from-the-request-a-resolver) is found and called: with no `Db.UseScope` open
there is nothing to ask, and a tenant-scoped read throws as it does in an app without one.

## What reads or writes without the filter

Worth knowing exactly, because each is a place a review should look:

| Path | What it does | Why |
|---|---|---|
| `Tenant.Across()` | reads every tenant's rows, and the rows that belong to none | the one deliberate way across; an insert inside it is refused unless the row already says its tenant |
| `IgnoreQueryFilters()` on EF Core's own `IQueryable` | lifts every filter, the tenant's included | EF Core's meaning of the call. `Invoice.IgnoreQueryFilters()` — Rask's — lifts soft delete only |
| raw SQL — `FromSql`, `ExecuteSql`, `SqlQuery` | whatever the SQL says | Rask does not rewrite SQL; a `FromSql` composed over an entity set still gets the filter |
| `BulkInsert` with `SkipChangeTracking` | writes rows with **no tenant stamp** | it replaces the change tracker, and the interceptors with it. Set `TenantId` on each row, or use the default path |
| a row whose `TenantId` the entity set itself | is saved into that tenant | the stamp fills an unset tenant and leaves a set one alone; only the entity can set it, through its private setter |
| a context of your own that does not call `ApplyRaskConventions(this)` | no filter, no stamp | the conventions are what add them |
| a second mapping of the same table — a legacy `DbContext` beside Rask's | that context's own rules | Rask filters what goes through its contexts and nothing else |

Everything else — `Where`, `Find`, `Count`, `Update`, `Delete`, `Save`, `AsQueryable()`, a child's own read
face, an `Include`, `ExecuteUpdate`/`ExecuteDelete` over a set — goes through the filter.

## On each provider

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| tenant filter | ✅ | ✅ | ✅ |
| account uniqueness per tenant | ✅ | ✅ | ✅ |

The accounts index is over a column that folds a null tenant to `Guid.Empty`, not over the nullable tenant
itself, because **NULL in a unique index is not portable**: SQLite and PostgreSQL treat two NULLs as
distinct — so any number of administrators could share one address — while SQL Server treats them as equal,
so only one could. Same schema, three behaviours, and the kind that passes every test on the default
provider. Folding the null away makes one ordinary index that behaves identically everywhere.
