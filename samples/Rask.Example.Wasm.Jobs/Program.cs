using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;
using Rask.Example.Wasm.Jobs;
using Rask.Jobs;
using Rask.SQLite.Browser;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

// AddLogging() is registered by the host but ships no provider, so ILogger output goes nowhere by
// default — a failed job or an exhausted storage quota would look exactly like nothing happening.
host.Services.AddLogging(b => b.AddProvider(new BrowserConsoleLoggerProvider()));

// Everything below this line is the registration you would write on a server, in the same order, with
// one browser-specific call at the top. That is the point of the sample.

// Makes /rask/app.db durable: restored from IndexedDB before anything opens it, written back on an
// interval and on page-hide, and owned by exactly one tab.
host.Services.AddRaskBrowserSqlite("app", o =>
{
    // Far shorter than the 30s default, because the interval IS the durability window: the browser does
    // not wait for the page-hide flush, so anything written since the last tick is lost on a reload. Two
    // seconds makes the demo's "queue a job, reload, it's still there" claim actually true. A real app
    // trades this against the cost of copying the whole database each tick.
    o.SnapshotInterval = TimeSpan.FromSeconds(2);
});

host.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(BrowserSqlite.ConnectionString("app")));

// Registered after the database and before the processor: registration order is start order, and a
// plain IHostedService finishes its work inside StartAsync, so the tables exist before the first poll.
host.Services.AddHostedService<SchemaInitializer>();

host.Services.AddSingleton<GreetingFeed>();
host.Services.AddSingleton<DatabaseReady>();
host.Services.AddRaskCqrs();

// Identical to the server. The processor is a BackgroundService, which until now nothing on this host
// would have started.
host.Services.AddRaskJobs<AppDbContext>(o =>
{
    o.PollInterval = TimeSpan.FromMilliseconds(250);
    o.MaxAttempts = 3;
});

await host.RunAsync<App>();
