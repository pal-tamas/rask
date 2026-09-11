// rask:ifnot wasm
using Company.RaskServer.Features.Shared;
// rask:end
// rask:if wasm data
using Company.RaskServer.Features.Shared;
// rask:end
using Microsoft.AspNetCore.HttpOverrides;
// rask:ifnot wasm
using Rask.Server;
using Rask.Server.Diagnostics;
// rask:end
// rask:if wasm cqrs data ops
using Rask.Server;
// rask:end
// rask:if push pwa
using Company.RaskServer.Features.Push;
using Rask.WebPush;
// rask:end
// rask:if cqrs
using Rask.Query;
// rask:end
// rask:if pwa
// rask:ifnot wasm
using Rask.Core.Browser;
// rask:end
// rask:end
// rask:if wasm
using Rask.Spa.Hosting;
// rask:if cqrs
using Rask.Cqrs.Server;
// rask:end
// rask:end
// rask:if cqrs
using Rask.Cqrs;
// rask:if data
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;
using Rask.SQLite;
using Microsoft.Data.Sqlite;
using Rask.SQLite.Litestream;
// rask:if jobs
using Rask.Jobs;
// rask:end
// rask:if mail
using Rask.Mail;
// rask:end
// rask:if cache
using Rask.Cache;
// rask:end
// rask:if outbox
using Rask.Outbox;
// rask:end
// rask:if snapshots
using Rask.SQLite.Snapshots;
// rask:end
// rask:end
// rask:end
// rask:if logs
using Rask.Logging;
// rask:end
// rask:if cqrs data ops
using Rask.Dashboard;
// rask:end

var builder = WebApplication.CreateBuilder(args);

// rask:ifnot wasm
// The languages this app ships, and the one place to change them. The FIRST is the default
// a visitor falls back to when nothing else matches; add another line to ship another.
//
// A visitor's language is negotiated per request -- ?culture= beats a remembered cookie,
// which beats the browser's Accept-Language -- and then belongs to their session, so it
// survives every render over the live socket. Nothing scaffolds a language switcher: see
// docs/localization.md for the ten-line component, and for why that is your call.
//
// Text comes from Resources/Strings.{culture}.json, compiled into typed members: a
// missing key is a build error rather than a blank on the page (docs/diagnostics.md).
builder.Services.AddRask(configureCulture: c =>
{
    c.SupportedCultures.Add("en");
});
// A liveness/readiness endpoint (mapped below) — `rask deploy` probes it to gate the blue-green
// swap, and any load balancer or orchestrator can use it too. AddRaskLiveSessions reports the
// live-session pool: Degraded at 80% of MaxSessions, Unhealthy once new sessions are being
// refused with 503 — so a host that is full says so instead of answering a bare "up". Add real
// dependency checks alongside it, e.g. .AddDbContextCheck<AppDbContext>().
builder.Services.AddHealthChecks().AddRaskLiveSessions();
// rask:end
// rask:if wasm
// This server renders no pages of its own. The app is the WebAssembly build of Client/, served below
// by UseRaskSpa; AddRaskSpaHost compresses what it serves and applies the defaults every Rask host
// gets — a persisted key ring, so a deploy does not sign everyone out, and a shutdown budget that fits
// under the deploy's SIGKILL.
builder.Services.AddRaskSpaHost();
// rask:if cqrs data ops
// The live runtime, for the one server-rendered part of this app: the operator dashboard.
builder.Services.AddRaskServer();
// rask:end
// A liveness/readiness endpoint (mapped below) — `rask deploy` probes it to gate the blue-green
// swap, and any load balancer or orchestrator can use it too. Add real dependency checks alongside
// it, e.g. .AddDbContextCheck<AppDbContext>().
builder.Services.AddHealthChecks();
// An API answers an unhandled exception with a problem-details body rather than an HTML page.
builder.Services.AddProblemDetails();
// rask:end

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
// rask:if cqrs
// CQRS mediator: one call registers every IQueryHandler/ICommandHandler/INotificationHandler in
// this assembly (source-generated, reflection-free — trim/AOT-safe). Inject IDispatcher to send
// messages; add pipeline behaviors with o.AddOpenBehavior(...). See docs/cqrs.md.
builder.Services.AddRaskCqrs();

// Server state over that dispatcher: dedup, staleness, background refetch, invalidation.
// Inject IQueryClient and render a Query<T> instead of fetching in OnInitializedAsync —
// scoped per live session, so one visitor's results are never handed to another.
// See docs/query.md.
builder.Services.AddRaskQuery();
// rask:if wasm
// The endpoint half of remote dispatch: the browser app in Client/ sends its messages here.
// rask:ifnot data
// An anonymous caller gets the same answer for a real message name as for a typo, so
// the endpoint cannot be walked to enumerate this app's messages.
// rask:end
// rask:if data
// Fails closed: every message is authenticated by default, [AllowAnonymous] is the only
// way past, and an anonymous caller gets the same answer for a real message name as for
// a typo — so the endpoint cannot be walked to enumerate this app's messages.
builder.Services.AddRaskCqrsServer();
// rask:end
// rask:end
// rask:if data
// The app's database, on its own disk — no external server. AddRaskData registers the
// auditing/soft-delete/concurrency/domain-event interceptors; UseRaskSqlite is a drop-in for
// UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys). The
// connection string defaults to a local app.db but honours a ConnectionStrings:App override —
// `rask deploy` sets that to a path on a mounted volume so the DB survives redeploys.
// For each entity, declare a class deriving from Model<TId> anywhere in the project — no
// DbSet property, no configuration class, no registration — then `rask db add <Name>` /
// `rask db update` to create and apply the migration.
// rask:end
//
// rask:ifnot data
// RequireAuthenticatedUser is OFF because this app has no database, so it has no
// accounts to require — left on, every message would answer 401 and nothing would
// work. Add --data (or a scheme of your own) and DELETE this argument: the default is
// on for a reason, and a message reachable by anyone is a decision worth making.
builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);
// rask:end
// rask:if data
// The generic overload is what names the context to the ambient database, so `Product.Add(…)`
// and `Product.Where(…)` know which one to open. The non-generic AddRaskData() registers only
// the interceptors, and Db.Configure below then has nothing to bind.
//
// strictTables makes SQLite enforce each column's declared type instead of coercing whatever
// it is handed — without it the text "lots" stores happily in an INTEGER column and surfaces
// as a cast error much later. It applies to tables as they are created, so it costs nothing
// here and is awkward to adopt once there is data. Drop it if you need a column type outside
// SQLite's INT/INTEGER/REAL/TEXT/BLOB/ANY.
builder.Services.AddRaskData<AppDbContext>();
var connectionString = builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db";
// rask:if outbox
// Transactional outbox: a domain event marked IOutboxEvent is written to the outbox table in
// the SAME transaction as the change that raised it, then relayed at-least-once by a
// background processor. Registering it is also what hands it domain-event delivery, so
// AddRaskData above needs no argument to match.
builder.Services.AddRaskOutbox<AppDbContext>();
// rask:end
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(connectionString, o => o.StrictTables = true)
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
// Accounts: register, sign in, sign out. Backed by ASP.NET Core Identity, reached through
// Rask's own IAuth so the same call works on the Server host, in WebAssembly and inside an
// island. The FIRST account to register becomes the administrator; while none exists, that
// registration needs the one-time token written to the startup log.
builder.Services.AddRaskAuth<AppDbContext>();
// rask:if jobs

// Durable background jobs on the app's own database — no broker, no Redis. Enqueue with IJob;
// a hosted worker polls, runs each job through its Rask.Cqrs handler, and retries with backoff.
// Schedule recurring work here: o.AddRecurring<PurgeJob>("purge", TimeSpan.FromHours(1), () => new());
builder.Services.AddRaskJobs<AppDbContext>();
// rask:end
// rask:if mail

// Transactional email queued on the app's own database and delivered off the request thread. The
// body is a Rask component, so it uses the same component model as the UI. With no SMTP configured
// the dev default writes each message to ./mail-pickup as an .eml file you can open — set o.Smtp
// (host/port/credentials) to send for real.
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "no-reply@example.com";
    o.PickupDirectory = builder.Configuration["Mail:PickupDirectory"] ?? "mail-pickup";
});
// rask:end
// rask:if cache

// A cache on the app's own database: the standard IDistributedCache (so ASP.NET session/output
// caching just works) plus a typed ICache with GetOrAddAsync and absolute/sliding expiry. A
// background purger sweeps expired rows.
builder.Services.AddRaskCache<AppDbContext>();
// rask:end
// rask:if snapshots

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
});
// rask:end

// rask:end
// rask:end
// rask:if logs
// The application log, kept in a database of its own so it survives the restart that hid it.
// This registers an ILoggerProvider, so it captures exactly what every other sink sees; log
// calls never wait on the disk (entries are buffered and written in batches), and retention
// drops them by age and by row count.
//
// A SEPARATE FILE, on purpose — unlike the other database-backed batteries this one does not
// map onto AppDbContext. Log lines arrive at machine rates, and the line you most want is the
// one written while a transaction is failing, which on the app's context would roll back with
// it. The trade-off: this file is NOT covered by `rask db backup` or Litestream, and log lines
// can contain secrets — treat it as sensitive and keep it on the same persistent volume as
// your database (`rask deploy` sets ConnectionStrings:Logs to a path on that volume).
// Tip: an EF Core app logs every SQL command at Information, which will dominate the store
// on the default settings. Either raise the floor for that category in Logging:LogLevel, or
// skip it here:  o => o.ExcludedCategories.Add("Microsoft.EntityFrameworkCore.Database")
builder.Services.AddRaskLogging(
    builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db");

// rask:end
// rask:if push pwa
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

// rask:end
// rask:if cqrs data ops
// An operator dashboard at /_rask over every battery's table: queue depth, dead letters and the
// errors behind them, cache contents, the log, and how this database is configured. A panel
// only appears for a battery this app actually registered — the Logs page keeps a live tail
// in memory, and gains a searchable History over the stored log when Rask.Logging is on.
builder.Services.AddRaskDashboard<AppDbContext>();

// WHO MAY OPERATE THE APP. The dashboard shows job payloads, stored email bodies and log
// lines, so it is gated on the ADMIN role — the one the first account to register holds.
// Requiring merely a signed-in user would open all of that to anyone who registered,
// which on an app with open registration is everyone.
builder.Services.AddAuthorization(o =>
    o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole(RaskRoles.Admin)));

// rask:end
// rask:if pwa
// rask:ifnot wasm
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
// rask:end
// rask:end

var app = builder.Build();
// rask:if cqrs data
// Point the ambient database at the context registered above. This is what lets a model be
// used from anywhere — Product.Where(…), Product.FindAsync(id) — with no DbContext injected.
Db.Configure(app.Services);
// rask:end
// FIRST: rewrite Request.Scheme/RemoteIpAddress from the proxy's headers, so everything below
// (HSTS, redirects, your own logging) sees the request the visitor actually made.
app.UseForwardedHeaders();

// Health endpoint next — as terminal middleware it short-circuits before UseHttpsRedirection,
// so /health answers 200 over plain HTTP. `rask deploy` probes it internally on http://…:8080
// (no X-Forwarded-Proto), where a redirected endpoint would 307 to a port nothing listens on.
app.UseHealthChecks("/health");

// Unhandled exceptions. ErrorBoundary already covers anything thrown inside a component tree;
// this catches everything outside it, which would otherwise be a bare 500 with an empty body.
// Non-Development only — the developer exception page is strictly more useful locally, and this
// one deliberately shows nothing about the exception.
if (!app.Environment.IsDevelopment())
{
    // rask:ifnot wasm
    app.UseExceptionHandler("/error");
    // rask:end
    // rask:if wasm
    app.UseExceptionHandler();
    // rask:end
}

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
// rask:if cqrs data
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

// rask:end
// rask:if push pwa
// Endpoints go here, before UseRask, so they read in one place. Order is not what makes them work:
// routing matches on precedence, and any route is more specific than the catch-all.
app.MapPushSubscriptions();

// rask:end
// rask:if wasm
// rask:if cqrs
// Answers the messages the browser app dispatches. Two endpoints, not one per message:
// GET and POST on /_rask/cqrs/request/{name}, the verb carrying what IQuery and ICommand
// already declare — so a command is 405 on GET and cannot be fired by a URL or a
// prefetch.
app.MapRaskCqrs();
// rask:end
// rask:if cqrs data ops
// The operator dashboard, rendered by this server under its own prefix. Every other route stays the
// browser app's.
app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}");
// rask:end
// The browser app in Client/: its build output in Development, its published bundle otherwise. Its
// fallback answers every route nothing above claims — which is what keeps a refresh or a deep link on a
// client-side route working — so it goes last.
app.UseRaskSpa();
// rask:end
// rask:ifnot wasm
// To host this app under a sub-path (e.g. behind a reverse proxy mapping
// /myapp/* → this server), pass pathBase. Every framework endpoint and
// emitted URL is scoped under the prefix; user-space routes stay unprefixed.
//   app.UseRask<App>(pathBase: "/myapp");
app.UseRask<App>();
// rask:end

app.Run();
