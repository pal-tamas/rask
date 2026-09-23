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

**Why a `const`.** Like the other four, it is read at compile time rather than reflected over, so a trimmed
publish cannot lose it and quietly fall back to "shared".

## Where the tenant comes from

**The signed-in user's.** The tenant is an outcome of authentication rather than an input to routing: signing
in finds the user, and the user says which tenant they belong to. It travels on the `ClaimsPrincipal` as the
`rask:tenant` claim (`Tenant.ClaimType`), and the data layer reads it back — which is what lets a page do this
with nothing passed to it:

```csharp
var invoices = await Invoice.Read.OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync();
```

A restored session carries the claim too, so a reconnect comes back in the same tenant. The same holds for
every HTTP request — a minimal API, a controller, a CQRS endpoint — because the host makes the request's
services ambient for the data layer after authentication has run.

`Current.Tenant` says which tenant that is, from anywhere, and `Current.RequiredTenant` throws when there is
none. The order it answers in is: an explicit `Tenant.Use` scope first, then the signed-in user's claim, and
`null` inside `Tenant.Across()`. See [`Current`](data.md#the-current-user--current) for the user it answers
alongside.

### Putting a user in a tenant

Rask.Auth records the tenant on the account row but does not decide it — your app does, usually when an
invitation is accepted or a company signs up. Every entity has a protected `RecordTenant`, so your `User` can
say what joining a tenant means, and the registration lambda sets it in the same insert:

```csharp
public sealed class User : Authenticatable
{
    public void JoinTenant(Guid tenant) => RecordTenant(tenant);
}

await auth.RegisterAsync(model.Email, model.Password, (User user) => user.JoinTenant(invite.TenantId), ReturnUrl);
```

A user with no tenant — an administrator — carries no claim, and is covered [below](#administrators).

## Saying which tenant explicitly

```csharp
using (Tenant.Use(acmeId))          // work as this tenant
{
    await Invoice.CreateAsync(model);
}

using (Tenant.Across())             // deliberately span tenants — an admin tool, a migration
{
    var total = await Invoice.Read.CountAsync();
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

private async Task LoadAsync()
{
    using (Tenant.Use(_tenant))
    {
        _invoices = await Invoice.Read.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }
}
```

A report over every tenant is `Tenant.Across()` — one greppable thing a reviewer can find.

## A read with no tenant throws

```csharp
await Invoice.Read.ToListAsync();   // InvalidOperationException, when nothing says which tenant
```

Deliberately, and it is the decision most worth understanding. Returning *nothing* would be safe against
leaks and **indistinguishable from an empty database** — the failure that costs the most time to find.
Returning *everything* would be the leak itself. So it refuses, and says how to say which tenant.

## `IgnoreQueryFilters()` does not cross tenants

```csharp
await Invoice.Read.IgnoreQueryFilters().ToListAsync();   // includes soft-deleted; SAME tenant
```

It means "include soft-deleted rows" and leaves the tenant filter exactly where it is, so an existing call
never quietly becomes a cross-tenant read the day an aggregate declares `Scope`. Crossing tenants is
`Tenant.Across()`. This uses EF Core 10's named query filters.

## Indexes are prefixed for you

`HasIndex(p => p.Sku).IsUnique()` on a partitioned table would otherwise mean "no two tenants may ever use
the same SKU", and the symptom is one tenant unable to create a row because a different tenant already has
it, with nothing in the code saying so. Rask puts `TenantId` at the front of every index on a tenant-scoped
entity, so uniqueness means *within this tenant* and the filtered query can use the index.

## Writes stamp it, and it never moves

A create records the tenant in flight — the signed-in user's, with no `Tenant.Use` needed. An update that
would move a row to another tenant is refused: copy the row into the other tenant instead. The column is never
on the generated form model, so a post cannot set it. Inserting a tenant-scoped row inside `Tenant.Across()`
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
static read can reach it. See [Wiring, when Rask is not hosting](data.md#wiring-when-rask-is-not-hosting).

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
