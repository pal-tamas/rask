using Rask.Cache;
using Rask.Jobs;
using Rask.Logging;
using Rask.Mail;
using Rask.Outbox;
using Rask.SQLite.Snapshots;
using Rask.WebPush;

namespace Rask;

/// <summary>
/// How this app differs from a default one.
/// </summary>
/// <remarks>
/// Every battery this package brings is <b>on</b>, so an app that configures nothing is a complete app
/// with all of them running. What is written here is only the exceptions: the ones this app does without,
/// and the ones it wants configured differently.
/// <example>
/// <code>
/// app.Configure(c =>
/// {
///     c.Jobs.Off();                                            // no background work in this app
///     c.Mail.Configure(o => o.From = "no-reply@example.com");
///     c.Snapshots.Configure(o => o.Retain = 7);
/// });
/// </code>
/// </example>
/// </remarks>
public sealed class RaskAppOptions
{
    /// <summary>The database and Rask.Data's EF Core interceptors. Every battery below it depends on this.</summary>
    public Battery Data { get; } = new();

    /// <summary>The source-generated CQRS mediator, and the query cache that rides with it.</summary>
    public Battery Cqrs { get; } = new();

    /// <summary>Durable background jobs on the app's own database.</summary>
    public Battery<JobOptions> Jobs { get; } = new();

    /// <summary>Transactional email queued on the app's own database.</summary>
    public Battery<MailOptions> Mail { get; } = new();

    /// <summary>A database-backed cache: the standard <c>IDistributedCache</c> plus a typed <c>ICache</c>.</summary>
    public Battery<CacheOptions> Cache { get; } = new();

    /// <summary>The transactional outbox for durable domain-event delivery.</summary>
    public Battery<OutboxOptions> Outbox { get; } = new();

    /// <summary>Server-sent Web Push (VAPID + RFC 8291).</summary>
    /// <remarks>
    /// Wired only once a VAPID key pair is configured: sending needs one, and a freshly scaffolded app has
    /// to run before anybody has generated any keys.
    /// </remarks>
    public Battery<WebPushOptions> Push { get; } = new();

    /// <summary>Scheduled point-in-time snapshots of the SQLite file.</summary>
    public Battery<SqliteSnapshotOptions> Snapshots { get; } = new();

    /// <summary>
    /// The durable log store. Alone among the batteries it does not need the database — it keeps a SQLite
    /// file of its own, so it survives the restart that hid the log you wanted.
    /// </summary>
    public Battery<RaskLoggingOptions> Logs { get; } = new();

    /// <summary>The operator dashboard at <c>/_rask</c>, over every battery's table.</summary>
    public Battery Ops { get; } = new();

    /// <summary>
    /// Whether a reverse proxy sits in front, so <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> should be
    /// trusted. Off by default.
    /// </summary>
    /// <remarks>
    /// <b>The one host default that stays opt-in.</b> Without it <c>Request.Scheme</c> is <c>http</c>
    /// behind a TLS-terminating proxy, so HSTS never emits and <c>RemoteIpAddress</c> is the proxy rather
    /// than the visitor. But trusting those headers from an arbitrary client lets it forge its own IP, and
    /// whether a proxy is really in front is a fact about the deployment that no code can check.
    /// <c>rask deploy</c> puts Caddy in front and turns this on; an app exposed directly must leave it off.
    /// </remarks>
    public bool BehindProxy { get; set; }

    /// <summary>The liveness/readiness endpoint. <c>rask deploy</c> probes it to gate the blue-green swap.</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>Where <c>UseExceptionHandler</c> sends an unhandled exception outside Development.</summary>
    public string ErrorPath { get; set; } = "/error";

    /// <summary>
    /// The connection string for the application database, and for anything derived from it — the
    /// Litestream replica's source path and the snapshot source among them.
    /// </summary>
    /// <remarks>
    /// Read from <c>ConnectionStrings:App</c> when unset, falling back to a local <c>app.db</c>.
    /// <c>rask deploy</c> sets that key to a path on the mounted volume, so the database outlives the
    /// container the same way the key ring does.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Work that must happen after the container is built but <b>before anything opens the database</b>.
    /// </summary>
    /// <remarks>
    /// The ordering is the point: a restore that runs after the first query has already lost, and the
    /// failure is a fresh empty database on a machine that was supposed to have recovered. Continuous
    /// backup fills this in for you when a replica URL is configured.
    /// </remarks>
    public Func<IServiceProvider, Task>? RunBeforeDatabaseOpensAsync { get; set; }
}
