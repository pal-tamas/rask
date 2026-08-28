namespace Rask.Server;

/// <summary>
/// How this app differs from a default one. Everything here has a working default, so an app that
/// configures nothing is a complete app — the block exists for the exceptions.
/// </summary>
public sealed class RaskAppOptions
{
    /// <summary>
    /// Whether a reverse proxy sits in front, so <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c> should
    /// be trusted. Off by default.
    /// </summary>
    /// <remarks>
    /// <b>This one stays opt-in on purpose, and is the only host default that does.</b> Without it
    /// <c>Request.Scheme</c> is <c>http</c> behind a TLS-terminating proxy, so HSTS never emits and
    /// <c>RemoteIpAddress</c> is the proxy rather than the visitor. But trusting those headers from an
    /// arbitrary client lets it forge its own IP, and whether a proxy is really in front is a fact about
    /// the deployment that no code can check. <c>rask deploy</c> puts Caddy in front and turns this on;
    /// an app exposed directly to the internet must leave it off.
    /// </remarks>
    public bool BehindProxy { get; set; }

    /// <summary>The liveness/readiness endpoint. <c>rask deploy</c> probes it to gate the blue-green swap.</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>Where <c>UseExceptionHandler</c> sends an unhandled exception outside Development.</summary>
    public string ErrorPath { get; set; } = "/error";

    /// <summary>
    /// Work that must happen after the container is built but <b>before anything opens the database</b> —
    /// in practice, restoring a SQLite file from its replica.
    /// </summary>
    /// <remarks>
    /// A seam rather than a feature, because the ordering is the whole point: a Litestream restore that
    /// runs after the first query has already lost the race, and the failure is a fresh empty database on
    /// a machine that was supposed to have recovered. <c>Rask.Server</c> cannot reference the SQLite
    /// packages, so the call stays in the app; this is where it belongs in the sequence.
    /// </remarks>
    public Func<IServiceProvider, Task>? RunBeforeDatabaseOpensAsync { get; set; }

    /// <summary>The synchronous form of <see cref="RunBeforeDatabaseOpensAsync"/>.</summary>
    internal Action<IServiceProvider>? RunBeforeDatabaseOpens =>
        RunBeforeDatabaseOpensAsync is null
            ? null
            : services => RunBeforeDatabaseOpensAsync(services).GetAwaiter().GetResult();
}
