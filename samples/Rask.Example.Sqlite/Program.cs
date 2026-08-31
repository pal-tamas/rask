using Microsoft.EntityFrameworkCore;
using Rask.Example.Sqlite;
using Rask.Example.Sqlite.Data;
using Rask.Server;
using Rask.SQLite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();

// The whole point of the sample: swap `UseSqlite` for `UseRaskSqlite` and every connection this
// context opens gets the production pragma set (WAL, foreign_keys, busy_timeout, …) applied
// on open. RASK_DB_PATH lets the E2E fixture point at an isolated temp file.
var dbPath = builder.Configuration["RASK_DB_PATH"] ?? "raskExampleSqlite.db";
builder.Services.AddDbContextFactory<DemoDbContext>(options =>
    options.UseRaskSqlite($"Data Source={dbPath}"));

// The raw ADO.NET counterpart to the EF context above (same database file): AddRaskSqlite registers an
// ISqlite whose InImmediateTransactionAsync runs each write in a
// BEGIN IMMEDIATE transaction, acquiring the write lock through a non-blocking, fair-interval
// retry — no thread is held while it waits. The second demo card exercises it.
builder.Services.AddRaskSqlite($"Data Source={dbPath}");

// Continuous backup with Litestream (Rask.SQLite.Litestream) — commented so `dotnet run` works without
// the litestream binary or a cloud replica. In production you'd enable this to stream the WAL to
// object storage and restore on a fresh host. See docs/sqlite.md#continuous-backup-with-litestream.
//   builder.Services.AddRaskSqliteLitestream(o =>
//   {
//       o.DatabasePath = dbPath;
//       o.ReplicaUrl = "s3://my-bucket/rask-sqlite";   // or abs:// (Azure Blob), gcs://, file:///…
//   });

var app = builder.Build();

// await app.Services.RestoreSqliteFromLitestreamAsync();   // restore before opening the DB (see above)

// Create the schema once at startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DemoDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.MapStaticAssets();
app.UseRouting();
app.UseRask<App>();

app.Run();
