using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Auth;
using Rask.Cache;
using Rask.Core.Browser;
using Rask.Cqrs;
using Rask.Dashboard;
using Rask.Dashboard.Panels;
using Rask.Data;
using Rask.Example.Shop.Features.Ops;
using Rask.Example.Shop.Features.Push;
using Rask.Example.Shop.Features.Shared;
using Rask.Jobs;
using Rask.Logging;
using Rask.Mail;
using Rask.Outbox;
using Rask.Server;
using Rask.Server.Diagnostics;
using Rask.SQLite;
using Rask.SQLite.Litestream;
using Rask.SQLite.Snapshots;
using Rask.WebPush;

var builder = WebApplication.CreateBuilder(args);

// The languages this app ships. The FIRST is the default a visitor falls back to when
// nothing else matches. Their language is negotiated per request -- ?culture= beats a
// remembered cookie, which beats the browser's Accept-Language -- and then belongs to
// their session, so it survives every render over the live socket.
//
// Text comes from Resources/Strings.{culture}.json, compiled into typed members: a
// missing key is a build error rather than a blank on the page (docs/diagnostics.md).
builder.Services.AddRask(configureCulture: c =>
{
    foreach (var language in new[] { "en" })
    {
        c.SupportedCultures.Add(language);
    }
});

// A liveness/readiness endpoint (mapped below) — `rask deploy` probes it to gate the blue-green
// swap, and any load balancer or orchestrator can use it too. AddRaskLiveSessions reports the
// live-session pool: Degraded at 80% of MaxSessions, Unhealthy once new sessions are being
// refused with 503 — so a host that is full says so instead of answering a bare "up". Add real
// dependency checks alongside it, e.g. .AddDbContextCheck<AppDbContext>().
builder.Services.AddHealthChecks().AddRaskLiveSessions();

// Behind a reverse proxy (`rask deploy` runs Caddy in front), the app sees the proxy's own
// address and a plain-HTTP request. Without this Request.Scheme is "http", so UseHsts never
// emits, RemoteIpAddress is the proxy rather than the visitor, and any redirect you build is
// wrong. The proxy's container IP is assigned by Docker and changes, so it can't be named in
// KnownProxies — clearing the lists is what makes this work, and it is safe in that topology
// because the container publishes no host port: only the proxy can reach it. If you expose this
// app directly to the internet, delete this block (a client could otherwise forge its own IP).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CQRS mediator: one call registers every IQueryHandler/ICommandHandler/INotificationHandler in
// this assembly (source-generated, reflection-free — trim/AOT-safe). Inject IDispatcher to send
// messages; add pipeline behaviors with o.AddOpenBehavior(...). See docs/cqrs.md.
builder.Services.AddRaskCqrs();
// The app's database, on its own disk — no external server. AddRaskData registers the
// auditing/soft-delete/concurrency/domain-event interceptors; UseRaskSqlite is a drop-in for
// UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys). The
// connection string defaults to a local app.db but honours a ConnectionStrings:App override —
// `rask deploy` sets that to a path on a mounted volume so the DB survives redeploys.
// `rask generate feature X …` adds its DbSet to AppDbContext (it attaches to the app's context);
// `rask db add <Name>` / `rask db update` create and apply the migration.
// Domain events go wherever delivery is owned: AddRaskOutbox below claims them, and this call needs
// no argument to match. The handover is resolved when the container is built, so the two calls work
// in either order.
builder.Services.AddRaskData();
var connectionString = builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db";
// Transactional outbox: a domain event marked IOutboxEvent is written to the outbox table in
// the SAME transaction as the change that raised it, then relayed at-least-once by a
// background processor. Registering it is also what hands it domain-event delivery.
builder.Services.AddRaskOutbox<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(connectionString)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// Continuous backup. Litestream streams the write-ahead log to object storage, which is what
// makes one box a safe place to keep your only copy: if the machine dies, a fresh one restores
// the database from the replica on startup and carries on. Durability stops depending on that
// one disk — the whole premise of running a real product on a single server.
//
// Inert until you point it somewhere. To turn it on:
//   rask deploy --env "Litestream__ReplicaUrl=s3://your-bucket/app"
// plus whatever credentials your provider needs (e.g. AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).
// s3://, gcs://, abs:// and file:// replicas are all supported — see docs/sqlite.md.
var replicaUrl = builder.Configuration["Litestream:ReplicaUrl"];
if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    builder.Services.AddRaskSqliteLitestream(o =>
    {
        o.DatabasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        o.ReplicaUrl = replicaUrl;
    });
}
// Durable background jobs on the app's own database — no broker, no Redis. Enqueue with IJob;
// a hosted worker polls, runs each job through its Rask.Cqrs handler, and retries with backoff.
// Schedule recurring work here: o.AddRecurring<PurgeJob>("purge", TimeSpan.FromHours(1), () => new());
builder.Services.AddRaskJobs<AppDbContext>();

// Transactional email queued on the app's own database and delivered off the request thread. The
// body is a Rask component, so it uses the same component model as the UI. With no SMTP configured
// the dev default writes each message to ./mail-pickup as an .eml file you can open — set o.Smtp
// (host/port/credentials) to send for real.
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "no-reply@example.com";
    o.PickupDirectory = builder.Configuration["Mail:PickupDirectory"] ?? "mail-pickup";
});

// A cache on the app's own database: the standard IDistributedCache (so ASP.NET session/output
// caching just works) plus a typed ICache with GetOrAddAsync and absolute/sliding expiry. A
// background purger sweeps expired rows.
builder.Services.AddRaskCache<AppDbContext>();

// Scheduled point-in-time backups, a second line of defence alongside the continuous replication
// above. Taken through SQLite's Online Backup API rather than a file copy — with WAL on, copying
// the .db can capture a torn database, because the committed data is split across the file and
// the -wal. Same connection string, so it follows a ConnectionStrings:App override.
builder.Services.AddRaskSqliteSnapshots(o =>
{
    o.DatabasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
    o.DestinationDirectory = builder.Configuration["Sqlite:SnapshotDirectory"] ?? "snapshots";
    o.Interval = TimeSpan.FromHours(6);
    o.Retain = 7;
    // Sample-only: take one immediately so the Ops page has something to show without waiting six
    // hours. Harmless in production too — it just means a fresh backup right after every deploy.
    o.SnapshotOnStartup = true;
});

// The application log, kept in a database of its own so it survives the restart that hid it. This
// registers an ILoggerProvider, so it captures exactly what every other sink sees; log calls never wait
// on the disk (entries are buffered and written in batches), and retention drops them by age and by row
// count.
//
// A SEPARATE FILE, on purpose — unlike the other database-backed batteries this one does not map onto
// AppDbContext. Log lines arrive at machine rates, and the line you most want is the one written while a
// transaction is failing, which on the app's context would roll back with it. The trade-off: this file is
// NOT covered by `rask db backup` or Litestream, and log lines can contain secrets — treat it as sensitive
// and keep it on the same persistent volume as your database (`rask deploy` sets ConnectionStrings:Logs to
// a path on that volume).
// This sample keeps EF Core's per-command logging out of the store: an EF app logs every SQL statement
// at Information, and on the default settings that is most of what you would find in here. The floor in
// Logging:LogLevel is the other lever.
builder.Services.AddRaskLogging(
    builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db",
    o => o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database"));

// Server-sent Web Push (VAPID + RFC 8291), no external service. Generate a key pair once with
// VapidKeys.Generate() and store it in configuration or user-secrets — the PUBLIC key is handed
// to the browser to subscribe with; the PRIVATE key signs and must never be served.
//
// Registered only once a key pair is configured: AddRaskWebPush validates its options and
// throws at startup without them, and a freshly scaffolded app has to run before you have
// generated any keys. The subscription store is registered either way so the endpoints and
// the UI compile and work; sending is what needs the keys.
var vapidPublicKey = builder.Configuration["WebPush:PublicKey"];
var vapidPrivateKey = builder.Configuration["WebPush:PrivateKey"];
if (!string.IsNullOrWhiteSpace(vapidPublicKey) && !string.IsNullOrWhiteSpace(vapidPrivateKey))
{
    builder.Services.AddRaskWebPush(o =>
    {
        o.VapidKeys = new VapidKeys(vapidPublicKey, vapidPrivateKey);
        o.Subject = builder.Configuration["WebPush:Subject"] ?? "mailto:admin@example.com";
    });
}

builder.Services.AddSingleton<PushSubscriptionStore>();

// An operator dashboard at /_rask over every pillar's table: queue depth, dead letters and the errors
// behind them, cache contents, the log, and how this database is configured. Its Logs page keeps a live
// tail in memory and — because Rask.Logging is registered above — also offers a searchable History over
// the stored log. Features/Ops/OpsPage.cs
// next door is the hand-rolled version of the same idea — it exists to show that the pillars really are
// just tables you can SELECT from. This is what you get without writing it.
builder.Services.AddRaskDashboard<AppDbContext>();

// Lights up the dashboard's Backup card. The dashboard can't read Litestream/snapshot state itself
// without dragging a native SQLite provider bundle into every consumer, so this app — which already
// uses both — supplies the reading. See Features/Ops/BackupProbe.cs.
builder.Services.AddSingleton<IDashboardBackupProbe, BackupProbe>();

// WHO MAY OPERATE THE APP. The dashboard exposes job payloads, stored email bodies and log lines, so it
// is gated on this policy. Without one it would deny everyone outside Development. The demo credential
// store has no roles, so this admits any signed-in user; a real app would require one.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole(RaskRoles.Admin)));

// The read-through cache accessor `rask generate cache` scaffolded. Scoped, like any component
// dependency; the ICache it wraps is backed by the same database as everything else.
builder.Services.AddScoped<Rask.Example.Shop.Features.Products.PopularProducts>();

// Installable PWA: AddRaskPwa serves the manifest + service worker and emits the manifest link +
// SW registration into the server-rendered <head>. The app is installable and push-capable, but NOT
// an offline app (a Server app renders over a live WebSocket) — offline navigations show wwwroot/
// offline.html. To send Web Push from this app, add Rask.WebPush; see docs/pwa.md.
builder.Services.AddRaskPwa(new WebAppManifest
{
    Name = "Rask App",
    ShortName = "Rask App",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
// Cookie auth — Rask reads HttpContext.User; the sign-in handshake sets this cookie on redeem.
// Accounts: register, sign in, sign out, on ASP.NET Core Identity. The cookie scheme comes with it, so
// RaskApp's UseAuthentication/UseAuthorization below find a scheme provider and wire themselves in the
// right order. The FIRST account to register becomes the administrator — which is what gates /_rask above.
builder.Services.AddRaskAuth<AppDbContext>();
var app = builder.Build();

// Create the schema and seed, BEFORE app.Run() starts the hosted processors. A real app runs
// `rask db add Init` / `rask db update` instead; this sample uses EnsureCreated so it can be cloned
// and run with no migration step. See DbInitializer for why the ordering matters.
// UserManager and RoleManager are scoped, so the seeding borrows a scope of its own — this runs before
// app.Run(), where there is no request to take one from.
using (var seedScope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(
        app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
        seedScope.ServiceProvider.GetRequiredService<UserManager<RaskUser>>(),
        seedScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
}

// FIRST: rewrite Request.Scheme/RemoteIpAddress from the proxy's headers, so everything below
// (HSTS, redirects, your own logging) sees the request the visitor actually made.
app.UseForwardedHeaders();

// Health endpoint next — as terminal middleware it short-circuits before UseHttpsRedirection,
// so /health answers 200 over plain HTTP. `rask deploy` probes it internally on http://…:8080
// (no X-Forwarded-Proto), where a redirected endpoint would 307 to a port nothing listens on.
app.UseHealthChecks("/health");

// Transport security (applies whether or not auth is enabled): redirect HTTP→HTTPS, and in
// non-Development emit HSTS so browsers refuse plain-HTTP for the configured max-age.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapStaticAssets();

// Give bare status codes (a 404 from an unmatched route) a readable body instead of a blank page.
app.UseStatusCodePages();
// Restore before anything opens the database (migrations, the first query). On a box that
// already has app.db this is a no-op and never clobbers it; on a fresh one it pulls the
// database back from the replica — which is the moment the "disposable box" promise is kept
// or broken. Guarded because RestoreSqliteFromLitestreamAsync throws when no replica is
// configured, and an app without one must still start.
if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    await app.Services.RestoreSqliteFromLitestreamAsync();
}
// Must precede UseRask so HttpContext.User is populated on the GET and the WS upgrade.
app.UseAuthentication();
app.UseAuthorization();

// Endpoints go here, before UseRask, so they read in one place. Order is not what makes them work:
// routing matches on precedence, and any route is more specific than the catch-all.
app.MapPushSubscriptions();

// To host this app under a sub-path (e.g. behind a reverse proxy mapping
// /myapp/* → this server), pass pathBase. Every framework endpoint and
// emitted URL is scoped under the prefix; user-space routes stay unprefixed.
//   app.UseRask<App>(pathBase: "/myapp");
app.UseRask<App>();

app.Run();
