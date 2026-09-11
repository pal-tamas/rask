// rask:if cqrs data
using Company.RaskServer.Features.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
// rask:if cache
using Rask.Cache;
// rask:end
// rask:if storage
using Rask.Storage;
// rask:end
// rask:end
using Rask.Cqrs;
using Rask.Cqrs.Server;
// rask:if cqrs data
using Rask.Data;
// rask:if jobs
using Rask.Jobs;
// rask:end
// rask:end
// rask:if logs
using Rask.Logging;
// rask:end
// rask:if cqrs data mail
using Rask.Mail;
// rask:end
using Rask.Meta.Hosting;
// rask:if cqrs data outbox
using Rask.Outbox;
// rask:end
// rask:if cqrs data
using Rask.SQLite;
using Rask.SQLite.Litestream;
// rask:if snapshots
using Rask.SQLite.Snapshots;
// rask:end
// rask:end
var builder = WebApplication.CreateBuilder(args);

// EVERY SETTING LIVES IN appsettings.json, under "Rask". Each call below reads its own section —
// AddRaskCqrsServer reads Rask:Cqrs:Server, AddRaskMeta reads Rask:Meta, AddRaskMail reads Rask:Mail —
// so this file says WHAT the app is made of and appsettings.json says how it is tuned. An environment
// variable overrides any key with double underscores (Rask__Mail__From), which is how `rask deploy`
// points a deployed app at its volume. A callback here still wins over both, for the rare value that
// has to be code. The full list of sections is in docs/configuration.md.
//
// AddRaskCqrsServer registers the mediator AND the endpoint pair the front end dispatches
// through. The TypeScript the front end imports is generated from these same message records
// at build time, so the two halves cannot disagree about a payload or a result.
//
// Dispatched messages do not require a signed-in caller in this template —
// Rask:Cqrs:Server:RequireAuthenticatedUser is false in appsettings.json, beside a note on when to
// turn it back on.
builder.Services.AddRaskCqrsServer();

builder.Services.AddSingleton<Company.RaskServer.Features.Hello.VisitCounter>();

// A liveness/readiness endpoint (mapped below). `rask deploy` probes it to gate the
// blue-green swap; also useful for any load balancer or orchestrator.
builder.Services.AddHealthChecks();

// The front end's Node server, supervised as a child process on loopback. The framework is
// the one the .csproj named — read from the assembly, so the build and the running host
// cannot disagree about which one was built.
//
// Set Rask:Meta:BaseUrl to this host's own loopback address if you dispatch from a SERVER render:
// a relative URL has no origin in Node, and the value is deliberately configured rather than
// derived from the incoming request.
builder.Services.AddRaskMeta();
// rask:if cqrs data
// The app's database, on its own disk — no external server. AddRaskData registers the
// auditing/soft-delete/concurrency/domain-event interceptors; UseRaskSqlite is a drop-in for
// UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys), and reads
// its connection string from Rask:ConnectionStrings:App — a local app.db in appsettings.json, which
// `rask deploy` points at a mounted volume so the DB survives redeploys.
// For each entity, declare a class deriving from Model<TId> anywhere in the project — no
// DbSet property, no configuration class, no registration — then `rask db add <Name>` /
// `rask db update` to create and apply the migration.
//
// The generic overload is what names the context to the ambient database, so `Product.Add(…)`
// and `Product.Where(…)` know which one to open. The non-generic AddRaskData() registers only
// the interceptors, and Db.Configure below then has nothing to bind.
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
// s3://, gcs://, abs:// and file:// replicas are all supported — see docs/sqlite.md.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Rask:Litestream:ReplicaUrl"]))
{
    builder.Services.AddRaskSqliteLitestream();
}
// Accounts: register, sign in, sign out. Backed by ASP.NET Core Identity, reached through
// Rask's own IAuth so the same call works on the Server host, in WebAssembly and inside an
// island. The FIRST account to register becomes the administrator; while none exists, that
// registration needs the one-time token written to the startup log.
builder.Services.AddRaskAuth<AppDbContext>();

// app.UseAuthorization() below needs the authorization services, and AddRaskAuth does not
// register them — it registers Identity and Rask's IAuth. Without this the host throws
// "Unable to find the required services" at startup, after a build that succeeded.
builder.Services.AddAuthorization();
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
var app = builder.Build();
// The endpoint pair every dispatched message arrives on: GET for queries, POST for commands,
// both under /_rask/cqrs/request/{name}. Two routes however many messages the app grows, and
// the verb carries what IQuery and ICommand already declare — so a command is 405 on GET and
// cannot be triggered by a URL, a prefetch or a link scanner.
//
// Mapped BEFORE UseRaskMeta. That call ends the pipeline with a fallback that forwards to the
// front end, so an endpoint added after it would be answered with a rendered page instead —
// which is the one failure of this lane that looks like a front-end bug.
app.MapRaskCqrs();

app.MapHealthChecks("/healthz");

// rask:if cqrs data
// Register, sign in, sign out, /me and the three recovery flows, at /api/auth.
//
// AddRaskAuth (above, with the database) registers the services; this is what puts the
// endpoints on the pipeline, and without it every call from the front end 404s. The
// client is already there: `import { login } from '@rask/browser/auth'`.
//
// Before UseRaskMeta for the same reason MapRaskCqrs is — that call forwards everything
// it has not already answered to the node process, which would render a page at these
// routes instead of answering JSON.
app.UseAuthentication();
app.UseAuthorization();
app.MapRaskAuth();

// rask:end
// rask:if storage
// The routes behind files.Url(id) and files.TemporaryUrlAsync(id, lifetime). Before UseRaskMeta for
// the same reason MapRaskAuth is: it forwards everything it has not answered to the node process.
app.MapRaskStorage();

// rask:end
// Serves the framework's built client assets from Kestrel (one hop less per asset, and the
// immutable cache headers written for you) and forwards everything else to the node process.
// Before the port answers, requests get 503 with Retry-After rather than a 502 from
// forwarding into a closed socket.
app.UseRaskMeta();

app.Run();

