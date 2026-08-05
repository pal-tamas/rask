namespace Rask.Cli;

/// <summary>
///     The single shutdown ladder <c>rask</c> deploys and scaffolds against. Every rung is derived from the
///     one above it, so the numbers cannot drift apart the way two hardcoded constants coupled only by a
///     comment can — which is exactly what <c>DeployCommand</c>'s 20 and the scaffolder's 15 used to be.
///     <code>
///     docker stop -t 20s                        ← DockerStopSeconds; SIGKILL lands here
///       └─ HostOptions.ShutdownTimeout 15s      ← HostShutdownSeconds = DockerStop − HostReserve
///            ├─ Kestrel in-flight request drain
///            ├─ Rask live-session drain          5s  (RaskServerOptions.ShutdownDrainTimeout)
///            └─ hosted services, stopped CONCURRENTLY (ServicesStopConcurrently in the scaffold):
///                 Litestream final WAL flush    10s  (LitestreamOptions.ShutdownGracePeriod)
///                 In-flight email send          10s  (MailOptions.ShutdownGracePeriod)
///                 In-flight job / outbox item    5s  (Job/OutboxOptions.ShutdownGracePeriod)
///     </code>
///     <para>
///         <b>Why the inner rungs are not constants here.</b> They live in their own NuGet packages, and
///         <c>Rask.Cli</c> deliberately does not reference <c>Rask.Jobs</c> / <c>Rask.Mail</c> /
///         <c>Rask.SQLite.Litestream</c> — it must not start. They are pinned instead by each package's own
///         default test and by the table in <c>docs/deployment.md</c>, which is the one place all six
///         numbers appear together. Inventing a shared package to hold four integers would be worse.
///     </para>
///     <para>
///         <b>Concurrency is what makes the arithmetic work.</b> Stopped sequentially (the .NET default) the
///         inner rungs <em>sum</em> — 10 + 10 + 5 + 5 = 30s against a 15s budget, so whichever hosted
///         service stops last gets no grace at all, decided by the order of <c>AddRaskX</c> calls in someone's
///         <c>Program.cs</c>. Stopped concurrently they overlap at <c>max(...) = 10s</c>, leaving real
///         headroom. That is why the scaffold sets <c>ServicesStopConcurrently</c> alongside the timeout.
///     </para>
/// </summary>
internal static class ShutdownBudget
{
    /// <summary>Seconds <c>docker stop -t</c> waits after SIGTERM before SIGKILL.</summary>
    internal const int DockerStopSeconds = 20;

    /// <summary>
    ///     Margin between the app finishing and the SIGKILL: container teardown, log flush, DI-container
    ///     disposal, EF connection teardown — work that happens after <c>Host.StopAsync</c> returns.
    /// </summary>
    internal const int HostReserveSeconds = 5;

    /// <summary>Seconds the app gives itself (<c>HostOptions.ShutdownTimeout</c>) — scaffolded into Program.cs.</summary>
    internal const int HostShutdownSeconds = DockerStopSeconds - HostReserveSeconds;

    /// <summary>
    ///     Seconds between <c>caddy reload</c> returning and the retiring container's SIGTERM.
    ///     <para>
    ///         Sized to Caddy's keep-alive pool turnover, not to the app's workload. <c>caddy reload</c>
    ///         returns as soon as the admin API applies the config, but Caddy still holds pooled
    ///         connections to the old upstream; a request it is about to write onto one of those at the
    ///         moment SIGTERM lands gets a broken connection, and with the default <c>lb_try_duration</c>
    ///         of 0 it is not retried — a 502 to a real user. Today there is literally zero time between
    ///         the two.
    ///     </para>
    ///     <para>
    ///         This is explicitly <b>not</b> for letting live sessions migrate. A WebSocket to the old
    ///         color persists until the app closes it, so the SIGTERM <em>is</em> the migration trigger;
    ///         delaying it only delays the reconnect. Draining those cleanly is the app's job, and is what
    ///         <c>RaskServerOptions.ShutdownDrainTimeout</c> does.
    ///     </para>
    /// </summary>
    internal const int PreStopDrainSeconds = 2;
}
