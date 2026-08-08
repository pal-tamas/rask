# Rask.SQLite.Browser

A real SQLite database inside a browser WebAssembly app — the same `Microsoft.Data.Sqlite`, and the same
Entity Framework Core on top of it, that you would run on a server — persisted across reloads.

## Why

A WASM app has no filesystem that survives a reload. SQLite still runs there: the native `e_sqlite3` is
linked into the app at publish, and the database file lives in the runtime's in-memory filesystem. What is
missing is durability, and one owner. This package supplies both.

- **Restored before anything opens it.** A hosted service reads the newest snapshot out of IndexedDB and
  writes the file during `StartAsync`, so a service registered after it finds a populated database rather
  than an empty one.
- **Written back on an interval and on page-hide**, through SQLite's Online Backup API
  (`Rask.SQLite.Snapshots`) — never an unsafe file copy of a database someone might be writing to.
- **Owned by exactly one tab.** Every tab has its own copy of the in-memory filesystem, so two of them
  would hold two divergent databases and the last to snapshot would silently overwrite the other. A Web
  Lock elects one owner; the others run unpersisted and say so.

## Use

```csharp
builder.Services.AddRaskBrowserSqlite("app");
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite(BrowserSqlite.ConnectionString("app")));

// From here on, nothing is browser-specific:
builder.Services.AddRaskJobs<AppDbContext>();
```

Register it **before** anything that opens the database — registration order is start order.

```csharp
builder.Services.AddRaskBrowserSqlite("app", o =>
{
    o.SnapshotInterval = TimeSpan.FromSeconds(10);  // the real durability window
    o.Retain = 2;                                   // each snapshot costs a full database in quota
});
```

## What to know before you rely on it

- **Entity Framework Core requires `PublishTrimmed=false`.** EF Core does not survive the trimmer in a
  browser build — it fails with a `MissingMethodException` on a generic instantiation the trimmer removed.
  `Microsoft.Data.Sqlite` on its own is reflection-free and trims fine.
- **The durability window is the snapshot interval, not the page-hide flush.** The browser does not wait
  for a `pagehide` handler, so a force-closed or crashed tab loses whatever changed since the last tick.
- **Non-owner tabs are not read-only — they are separate.** They get their own empty in-memory database
  and never persist. Promoting a waiting tab when the owner closes, and proxying writes to the owner, are
  not implemented.
- Snapshots cost their full database size in the origin's storage quota, times `Retain`.
- Add `<NoWarn>$(NoWarn);WASM0001</NoWarn>` if you build with warnings as errors: the SQLite native build
  reports two varargs functions (`sqlite3_config`, `sqlite3_db_config`) that WASM cannot call. Neither is
  reached on the normal open/query/update paths.

## Links

- [Repository](https://github.com/pal-tamas/rask)
- [SQLite guide](https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md)
