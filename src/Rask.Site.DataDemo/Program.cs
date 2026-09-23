using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;
using Rask.Data;
using Rask.Query;
using Rask.Site.DataDemo;
using Rask.SQLite;
using Rask.SQLite.Browser;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

// Warnings and up in the browser console: EF Core logs every command it runs, and the snapshotter every tick.
host.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

// The database: a SQLite file in the tab, restored from IndexedDB before anything opens it and written back every
// two seconds — a small database, so the whole-file copy is cheap and a reload right after a save keeps it. No
// persistence prompt at boot: this app runs inside a guide's iframe, which is no moment to ask.
host.Services.AddRaskBrowserSqlite("notes", o =>
{
    o.SnapshotInterval = TimeSpan.FromSeconds(2);
    o.RequestPersistentStorage = false;
});

// Rask.Data over it: the interceptors, and NotesDb named as the context Note.CreateAsync writes through.
host.Services.AddRaskData<NotesDb>();
host.Services.AddDbContextFactory<NotesDb>((sp, o) => o
    .UseSqlite(BrowserSqlite.ConnectionString("notes"))
    .UseRaskFullTextSearch()
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// The read faces (Note.Read) query through a context of their own, on the same database.
host.Services.AddDbContextFactory<RaskReadDbContext>(o => o
    .UseSqlite(BrowserSqlite.ConnectionString("notes"))
    .UseRaskFullTextSearch());

// The query cache, and the one line that makes a write refresh it.
host.Services.AddRaskCqrs();
host.Services.AddRaskQuery();
host.Services.AddScoped<IDataChanges, QueryDataChanges>();

// Registration order is start order: the schema is built after the browser database is restored.
host.Services.AddSingleton<NotesReady>();
host.Services.AddHostedService<NotesDatabase>();

await host.RunAsync<App>();
