namespace Rask.Cli;

/// <summary>
///     The single shutdown ladder <c>rask</c> deploys against. One number decides it: the app's
///     <c>"stopTimeout"</c> in <c>.rask/deploy.json</c> (default 30 minutes). Every rung below is derived from it,
///     so the deploy's wait and the app's own budget cannot drift apart.
///     <code>
///     docker stop -t stopTimeout (30m)            ← the deploy waits this long; SIGKILL lands here
///       └─ HostOptions.ShutdownTimeout 29m55s     ← HostShutdownSeconds = stopTimeout − HostReserve, passed to
///            │                                       the app as ASPNETCORE_SHUTDOWNTIMEOUTSECONDS
///            ├─ Kestrel in-flight request drain   (stops accepting at once; traffic is already on the new colour)
///            ├─ Rask live-session drain          5s  (RaskServerOptions.ShutdownDrainTimeout)
///            └─ hosted services, stopped CONCURRENTLY (ServicesStopConcurrently, set by AddRask):
///                 Litestream final WAL flush    10s  (LitestreamOptions.ShutdownGracePeriod)
///                 In-flight email send          10s  (MailOptions.ShutdownGracePeriod)
///                 In-flight outbox item          5s  (OutboxOptions.ShutdownGracePeriod)
///                 In-flight job                 until the host's budget ends (JobsOptions.ShutdownGracePeriod
///                                                = infinite): no new job is claimed, the running one finishes
///     </code>
///     <para>
///         <b>Why the inner rungs are not constants here.</b> They live in their own NuGet packages, and
///         <c>Rask.Cli</c> deliberately does not reference <c>Rask.Jobs</c> / <c>Rask.Mail</c> /
///         <c>Rask.SQLite.Litestream</c>. The app learns its budget from the environment the deploy starts it
///         with, and the table in <c>docs/deployment.md</c> is the one place every rung appears together.
///     </para>
///     <para>
///         <b>The wait costs nothing in traffic.</b> The proxy points at the new colour before the old one is
///         stopped, and the old one stops waiting the moment its last job finishes — the budget is a ceiling, not
///         a sleep. Only the bare-port path, which has no proxy to switch, is down while the old container drains.
///     </para>
/// </summary>
internal static class ShutdownBudget
{
    /// <summary>The stop budget of an app whose <c>.rask/deploy.json</c> names none.</summary>
    internal static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromMinutes(30);

    /// <summary>The smallest budget accepted: it still leaves the app a few seconds after <see cref="HostReserveSeconds"/>.</summary>
    internal static readonly TimeSpan MinimumStopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The largest budget accepted — a deploy that may wait a day is already a mistake.</summary>
    internal static readonly TimeSpan MaximumStopTimeout = TimeSpan.FromHours(24);

    /// <summary>
    ///     Margin between the app finishing and the SIGKILL: container teardown, log flush, DI-container
    ///     disposal, EF connection teardown — work that happens after <c>Host.StopAsync</c> returns.
    /// </summary>
    internal const int HostReserveSeconds = 5;

    /// <summary>Seconds the app gives itself (<c>HostOptions.ShutdownTimeout</c>) inside <paramref name="stopTimeout"/>.</summary>
    internal static int HostShutdownSeconds(TimeSpan stopTimeout) => (int)stopTimeout.TotalSeconds - HostReserveSeconds;

    /// <summary>
    ///     Seconds between <c>caddy reload</c> returning and the retiring container's SIGTERM.
    ///     <para>
    ///         Sized to Caddy's keep-alive pool turnover, not to the app's workload. <c>caddy reload</c>
    ///         returns as soon as the admin API applies the config, but Caddy still holds pooled
    ///         connections to the old upstream; a request it is about to write onto one of those at the
    ///         moment SIGTERM lands gets a broken connection, and with the default <c>lb_try_duration</c>
    ///         of 0 it is not retried — a 502 to a real user.
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
