using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.SQLite;
using Rask.SQLite.Browser;
using Rask.SQLite.Browser.Fixture.Wasm;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

// Exactly what a browser app writes: the durable database first, then a plain UseSqlite with the one opt-in call.
host.Services.AddRaskBrowserSqlite("app");
host.Services.AddDbContextFactory<ArticleContext>(o => o
    .UseSqlite(BrowserSqlite.ConnectionString("app"))
    .UseRaskFullTextSearch());

// Registration order is start order: the schema exists before the page asks for it.
host.Services.AddSingleton<SchemaReady>();
host.Services.AddHostedService<ArticleSchema>();

await host.RunAsync<App>();
