using Microsoft.EntityFrameworkCore;
using Rask.Cache;
using Rask.Example.EfCore;
using Rask.Example.EfCore.Features.Catalog.Shared;
using Rask.Mail;
using Rask.Server;
using Rask.SQLite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();

// IDbContextFactory rather than a scoped DbContext: a Rask Server live session is long-lived over
// a WebSocket, so a session-scoped DbContext would outlive any unit of work (and a DbContext is
// neither thread-safe nor meant to be long-lived). Each slice opens a short-lived context per
// operation via the factory. RASK_DB_PATH lets the E2E fixture point at an isolated temp file.
var dbPath = builder.Configuration["RASK_DB_PATH"] ?? "raskExampleCatalog.db";
// UseRaskSqlite rather than UseSqlite: same provider, but every connection opens with the production
// pragmas (WAL journal, foreign_keys on, a busy_timeout) instead of SQLite's bare defaults.
builder.Services.AddDbContextFactory<CatalogDbContext>(options =>
    options.UseRaskSqlite($"Data Source={dbPath}"));

// Transactional email on the same SQLite database. No SMTP here: PickupDirectory makes the background
// MailProcessor write each sent message as an .eml file (RASK_MAIL_PICKUP lets the E2E fixture point at an
// isolated temp dir), and a short poll keeps the demo responsive. Set o.Smtp in production to send for real.
builder.Services.AddRaskMail<CatalogDbContext>(o =>
{
    o.From = "demo@rask.example";
    o.FromName = "Rask EF Core demo";
    o.PickupDirectory = builder.Configuration["RASK_MAIL_PICKUP"] ?? "mail-pickup";
    o.PollInterval = TimeSpan.FromSeconds(1);
});

// A read-through cache on the same SQLite database — GetOrCreateAsync stores each result as a CacheEntry row
// and the background CachePurger sweeps expired rows (a short interval keeps the demo tidy).
builder.Services.AddRaskCache<CatalogDbContext>(o => o.PurgeInterval = TimeSpan.FromSeconds(30));

var app = builder.Build();

// Create the schema and seed sample data once at startup.
await CatalogSeeder.SeedAsync(app.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>());

// MapStaticAssets serves wwwroot + package _content as routed endpoints (outrank the SPA catch-all).
app.MapStaticAssets();
app.UseRouting();
app.UseRask<App>();

app.Run();
