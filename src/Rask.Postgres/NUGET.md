# Rask.Postgres

**PostgreSQL for .NET apps on Entity Framework Core.** `UseRaskPostgres(...)` is a drop-in replacement for
`UseNpgsql` that gives every session the production timeouts — `statement_timeout`, `lock_timeout`,
`idle_in_transaction_session_timeout` — and turns on Npgsql's transient-failure retrying.

SQLite remains Rask's default, and for most single-developer products it is the right answer for a long
time. This package is the door out of one box: a managed database, a read replica, or more app instances
than one file can serve.

## Install

```bash
dotnet add package Rask.Postgres
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskPostgres(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

The connection string is `Rask:ConnectionStrings:App` in configuration — in production usually the whole
string, password included, as `Rask__ConnectionStrings__App` in the environment — and a missing one is an
error naming that key.

Override any default in the `Rask:Postgres` section:

```jsonc
{ "Rask": { "Postgres": { "StatementTimeout": "00:00:10", "LockTimeout": "00:00:03", "Retry": { "MaxCount": 3 } } } }
```

or through the optional configure delegate, which runs after the section and wins:

```csharp
o.UseRaskPostgres(sp, p =>
{
    p.StatementTimeout = TimeSpan.FromSeconds(10);
    p.LockTimeout = TimeSpan.FromSeconds(3);
    p.Retry.MaxCount = 3;
});
```

## What the defaults do

| Setting | Default | Why |
|---|---|---|
| `StatementTimeout` | 30s | A runaway query otherwise runs until the client disconnects. |
| `LockTimeout` | 10s | The analogue of SQLite's `busy_timeout`. Without it, a statement stuck behind a lock reports as a *slow query*, which sends you debugging the wrong thing. Must be below `StatementTimeout`. |
| `IdleInTransactionSessionTimeout` | 1m | An idle-in-transaction session keeps its locks and blocks `VACUUM` from reclaiming dead rows — the usual way a healthy database quietly bloats. |
| `Retry.Enabled` / `MaxCount` / `MaxDelay` | on / 6 / 30s | Npgsql's own `EnableRetryOnFailure`, which already knows which PostgreSQL error codes are transient. |

Set a timeout to `TimeSpan.Zero` to leave it to the server, so a server-level or role-level setting wins
instead of being overwritten.

The timeouts travel as startup parameters in the connection string (`Options=-c statement_timeout=…`), so they
are the session's defaults: they survive the pool resetting a returned connection, cost no round trip per
query, and reach code that opens the `DbConnection` itself. A `-c` your connection string already carries wins.
Behind PgBouncer in transaction mode, add `options` to its `ignore_startup_parameters`, or set the timeouts to
`TimeSpan.Zero` and configure them on the database role.

Retrying is an execution strategy, and like every EF Core retrying strategy it refuses a transaction you
opened yourself outside it: wrap a hand-written `BeginTransaction` in
`context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set `Rask:Postgres:Retry:Enabled` to `false`.
`SaveChanges`, and Rask's jobs, mail, outbox and cache, need nothing.

## What stays behind

Litestream continuous backup and file snapshots (`Rask.SQLite.Litestream`, `Rask.SQLite.Snapshots`) are
SQLite-only by definition — they replicate and copy *a file*. On PostgreSQL, backup is your provider's
snapshots or `pg_dump`. Migrations are unchanged: `dotnet ef` (and `rask db`) are provider-agnostic.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/data.md#postgresql>
