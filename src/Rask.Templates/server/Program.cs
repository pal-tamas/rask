using Company.RaskServer.Features.Shared;
using Microsoft.AspNetCore.HttpOverrides;
using Rask.Server;
using Rask.Server.Diagnostics;
// rask:if push pwa
using Company.RaskServer.Features.Push;
using Rask.WebPush;
// rask:end
// rask:if cqrs
using Rask.Query;
// rask:end
// rask:if pwa
using Rask.Core.Browser;
// rask:end
// rask:if wasm
using Rask.Wasm.Hosting;
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
// rask:if storage
using Rask.Storage;
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

// EVERY SETTING LIVES IN appsettings.json, under "Rask". Each call below reads its own section —
// AddRask reads Rask:Server and Rask:Culture, AddRaskMail reads Rask:Mail, and so on — so this file
// says WHAT the app is made of and appsettings.json says how it is tuned. An environment variable
// overrides any key with double underscores (Rask__Mail__From), which is how `rask deploy` points a
// deployed app at its volume. A callback here — AddRaskMail(o => …) — still wins over both, for the
// rare value that has to be code. The full list of sections is in docs/configuration.md.
//
// The languages this app ships are Rask:Culture:SupportedCultures; the FIRST is the default a visitor
// falls back to. A visitor's language is negotiated per request -- ?culture= beats a remembered
// cookie, which beats the browser's Accept-Language -- and then belongs to their session, so it
// survives every render over the live socket. Nothing scaffolds a language switcher: see
// docs/localization.md for the ten-line component, and for why that is your call.
//
// Text comes from Resources/Strings.{culture}.json, compiled into typed members: a
// missing key is a build error rather than a blank on the page (docs/diagnostics.md).
builder.Services.AddRask();
// rask:if wasm
// Serves the browser bundle this project publishes into wwwroot. Registered here and
// mapped below, before UseRouting.
builder.Services.AddRaskWasmHost();
// rask:end
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
// The endpoint half of remote dispatch, for the pages that move into the browser.
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
// UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys), and reads
// its connection string from Rask:ConnectionStrings:App — a local app.db in appsettings.json, which
// `rask deploy` points at a mounted volume so the DB survives redeploys.
// For each entity, declare a class deriving from Model<TId> anywhere in the project — no
// DbSet property, no configuration class, no registration — then `rask db add <Name>` /
// `rask db update` to create and apply the migration.
// rask:end
// rask:if data
//
// rask:end
// rask:if wasm
// rask:ifnot data
//
// Remote dispatch is open to anonymous callers in this app: Rask:Cqrs:Server:RequireAuthenticatedUser
// is false in appsettings.json, because an app with no database has no accounts to require. See the
// note beside that key before you ship.
builder.Services.AddRaskCqrsServer();
// rask:end
// rask:end
// rask:if data
// The generic overload is what names the context to the model surface, so `Product.Where(…)`
// and the generated `Product.CreateAsync(model)` know which one to open. The non-generic
// AddRaskData() registers only the interceptors, and Db.Configure below then has nothing to bind.
builder.Services.AddRaskData<AppDbContext>();
// rask:if outbox
// Transactional outbox: a domain event marked IOutboxEvent is written to the outbox table in
// the SAME transaction as the change that raised it, then relayed at-least-once by a
// background processor. Registering it is also what hands it domain-event delivery, so
// AddRaskData above needs no argument to match.
builder.Services.AddRaskOutbox<AppDbContext>();
// rask:end
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

// Continuous backup. Litestream streams the write-ahead log to object storage, which is what
// makes one box a safe place to keep your only copy: if the machine dies, a fresh one restores
// the database from the replica on startup and carries on. Durability stops depending on that
// one disk — the whole premise of running a real product on a single server.
//
// Inert until you point it somewhere. To turn it on:
//   rask deploy --env "Rask__Litestream__ReplicaUrl=s3://your-bucket/app"
// plus whatever credentials your provider needs (e.g. AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).
// s3://, gcs://, abs:// and file:// replicas are all supported — see docs/sqlite.md. The rest of
// Rask:Litestream (verification, restart delays) is optional.
var replicaUrl = builder.Configuration["Rask:Litestream:ReplicaUrl"];
if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    builder.Services.AddRaskSqliteLitestream();
}
// Accounts: register, sign in, sign out. Backed by ASP.NET Core Identity, reached through
// Rask's own IAuth so the same call works on the Server host, in WebAssembly and inside an
// island. The FIRST account to register becomes the administrator; while none exists, that
// registration needs the one-time token written to the startup log.
builder.Services.AddRaskAuth<AppDbContext>();
// rask:if jobs

// Durable background jobs on the app's own database — no broker, no Redis. Enqueue with IJob;
// a hosted worker polls, runs each job through its Rask.Cqrs handler, and retries with backoff.
// Schedule recurring work here — a schedule is code, not configuration:
//   builder.Services.AddRaskJobs<AppDbContext>(o => o.AddRecurring<PurgeJob>("purge", TimeSpan.FromHours(1), () => new()));
builder.Services.AddRaskJobs<AppDbContext>();
// rask:end
// rask:if mail

// Transactional email queued on the app's own database and delivered off the request thread. The
// body is a Rask component, so it uses the same component model as the UI. Rask:Mail holds the From
// address and, with no Rask:Mail:Smtp section, a pickup directory where each message lands as an
// .eml file you can open — add Smtp (host/port/user, password in the environment) to send for real.
builder.Services.AddRaskMail<AppDbContext>();
// rask:end
// rask:if cache

// A cache on the app's own database: the standard IDistributedCache (so ASP.NET session/output
// caching just works) plus a typed ICache with GetOrAddAsync and absolute/sliding expiry. A
// background purger sweeps expired rows.
builder.Services.AddRaskCache<AppDbContext>();
// rask:end
// rask:if storage

// The files your users upload, kept by id: save a RaskFile with IFiles.SaveAsync, keep the returned
// Id on your entity, and hand the file back with files.Url(id), files.TemporaryUrlAsync(id, lifetime)
// or files.Download(id). The bytes go to ./storage here and to /data/files on the deploy volume —
// which NO backup covers — until you point them at a bucket: rask deploy --env Storage__Provider=S3
// --env Storage__S3__Bucket=... (and the keys beside it), or Storage__Provider=Azure. The routes that
// serve the links are mapped further down, by app.MapRaskStorage().
builder.Services.AddRaskStorage<AppDbContext>();
// rask:end
// rask:if snapshots

// Scheduled point-in-time backups, a second line of defence alongside the continuous replication
// above. Taken through SQLite's Online Backup API rather than a file copy — with WAL on, copying
// the .db can capture a torn database, because the committed data is split across the file and
// the -wal. It snapshots the database behind Rask:ConnectionStrings:App; where to, how often and how
// many to keep are Rask:Snapshots.
builder.Services.AddRaskSqliteSnapshots();
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
// your database (`rask deploy` sets Rask__ConnectionStrings__Logs to a path on that volume).
// Tip: an EF Core app logs every SQL command at Information, which will dominate the store
// on the default settings. Either raise the floor for that category in Logging:LogLevel, or
// add "Microsoft.EntityFrameworkCore.Database" to Rask:Logging:ExcludedCategories.
builder.Services.AddRaskLogging();

// rask:end
// rask:if push pwa
// Server-sent Web Push (VAPID + RFC 8291), no external service. Generate a key pair once with
// VapidKeys.Generate() and store it in user-secrets or the environment under
// Rask:WebPush:VapidKeys — the PUBLIC key is handed to the browser to subscribe with; the PRIVATE
// key signs and must never be served. The contact address is Rask:WebPush:Subject.
//
// Registered only once a key pair is configured: AddRaskWebPush validates its options and
// refuses to start without them, and a freshly scaffolded app has to run before you have
// generated any keys. The subscription store is registered either way so the endpoints and
// the UI compile and work; sending is what needs the keys.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Rask:WebPush:VapidKeys:PublicKey"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Rask:WebPush:VapidKeys:PrivateKey"]))
{
    builder.Services.AddRaskWebPush();
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

var app = builder.Build();
// rask:if cqrs data
// Point the model surface at the context registered above. This is what lets a model be read
// and saved from anywhere — Product.Where(…), Product.CreateAsync(model) — with no DbContext
// injected.
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
    app.UseExceptionHandler("/error");
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
// The browser bundle, served from this app's own wwwroot.
//
// BEFORE UseRouting, and UseRouting written out rather than left implicit -- both matter.
// Routing selects an endpoint before the static-file middleware runs, and that middleware
// steps aside when one is already selected, so mapping the bundle afterwards lets the
// Rask catch-all answer /_framework/*.wasm with text/html -- which the browser reports as
// a broken WebAssembly module, nowhere near the ordering that caused it. And
// WebApplication inserts UseRouting at the START of the pipeline when nobody calls it,
// which would put routing ahead of this line however early it appears.
app.UseRaskWasmAssets();
app.UseRouting();
// rask:if cqrs
// Answers the messages the browser half dispatches. Two endpoints, not one per message:
// GET and POST on /_rask/cqrs/request/{name}, the verb carrying what IQuery and ICommand
// already declare — so a command is 405 on GET and cannot be fired by a URL or a
// prefetch. Mapped BEFORE UseRask, whose catch-all would otherwise answer these.
app.MapRaskCqrs();
// rask:end
// rask:end
// To host this app under a sub-path (e.g. behind a reverse proxy mapping
// /myapp/* → this server), set Rask:Live:PathBase in appsettings.json. Every framework endpoint
// and emitted URL is scoped under the prefix; user-space routes stay unprefixed.
app.UseRask<App>();

// rask:if storage
// The routes behind files.Url(id) and files.TemporaryUrlAsync(id, lifetime). After UseRask, which sets
// the path base they live under.
app.MapRaskStorage();

// rask:end
app.Run();
