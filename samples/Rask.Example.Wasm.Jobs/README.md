# Rask.Example.Wasm.Jobs

Background jobs running in the browser, against a real SQLite database, with no server behind them.

```bash
dotnet publish samples/Rask.Example.Wasm.Jobs -c Release
# then serve bin/Release/net10.0-browser/publish/wwwroot with any static file server
```

Queue a job, watch a `BackgroundService` pick it up and write a row, then reload the page and see the
row still there.

## What it proves

Every line of `Program.cs` below the first call is the registration you would write on a server:

```csharp
host.Services.AddRaskBrowserSqlite("app", o => o.SnapshotInterval = TimeSpan.FromSeconds(2));

host.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(BrowserSqlite.ConnectionString("app")));
host.Services.AddHostedService<SchemaInitializer>();
host.Services.AddRaskCqrs();
host.Services.AddRaskJobs<AppDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(250));
```

`GreetJob` is an `IJob`, its handler is an ordinary `ICommandHandler<GreetJob>`, and both would compile
and run unchanged on `Rask.Server`. The job tables are the same tables (`modelBuilder.AddRaskJobs()`),
claimed with the same lease, retried with the same backoff.

The handler raises `GreetingFeed`, and the page repaints from that — an out-of-band render triggered by
the processor's poll loop, with no click anywhere near it.

## Four things worth reading the code for

- **`PublishTrimmed=false`, and why this is its own project.** EF Core does not survive the trimmer in a
  browser build; the showcase's `TrimMode=full` zero-IL-warning gate is load-bearing and must stay intact.
  `Microsoft.Data.Sqlite` on its own trims fine — it is EF that does not.
- **It cannot be published with `-p:WasmBuildNative=false`.** SQLite is a native library; skipping the
  relink yields a bundle that boots and then fails on every database call.
- **`DatabaseReady`.** Hosted services start at the *end* of boot on this host, so `OnMountAsync` runs
  before the schema exists. Registration order makes one service *start* before another, not become
  *ready* — so readiness is an explicit signal, as `docs/lifecycle.md` recommends.
- **The snapshot interval is the durability window.** The browser does not wait for the `pagehide` flush,
  so anything written since the last tick is lost on a reload. This sample uses 2s to make its own claim
  true; a real app trades that against copying the whole database each tick.

## Open a second tab

Only one tab may own the database, so the second gets its own empty one — and without being told, that is
indistinguishable from your data having been deleted. This sample injects `BrowserSqliteOwnership` and
shows a banner instead. Note it waits for `ownership.Resolved` before rendering it: `IsOwner` is `null`
while the election is in flight, so "still deciding" and "not the owner" stay distinct and a normal boot
never flashes a warning.

What is still not implemented: promoting a waiting tab when the owner closes, and proxying a non-owner's
writes to the owner.
