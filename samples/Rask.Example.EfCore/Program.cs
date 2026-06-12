using Microsoft.EntityFrameworkCore;
using Rask.Example.EfCore;
using Rask.Example.EfCore.Features.Catalog.Shared;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();

// IDbContextFactory rather than a scoped DbContext: a Rask Server live session is long-lived over
// a WebSocket, so a session-scoped DbContext would outlive any unit of work (and a DbContext is
// neither thread-safe nor meant to be long-lived). Each slice opens a short-lived context per
// operation via the factory. RASK_DB_PATH lets the E2E fixture point at an isolated temp file.
var dbPath = builder.Configuration["RASK_DB_PATH"] ?? "raskExampleCatalog.db";
builder.Services.AddDbContextFactory<CatalogDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// Create the schema and seed sample data once at startup.
await CatalogSeeder.SeedAsync(app.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>());

// UseStaticFiles before UseRouting so static files win over Rask's catch-all route.
app.UseStaticFiles();
app.UseRouting();
app.UseRask<App>();

app.Run();
