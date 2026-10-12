using Microsoft.Extensions.Logging;

namespace Rask.Dashboard;

/// <summary>Options for the batteries dashboard.</summary>
public sealed class OpsOptions
{
    /// <summary>
    /// Which actions the dashboard offers. Defaults to <see cref="RaskDashboardActions.Safe"/> — the
    /// destructive tier is deliberately not on by default, even for an operator page behind a policy.
    /// </summary>
    public RaskDashboardActions Actions { get; set; } = RaskDashboardActions.Safe;

    /// <summary>
    /// How often an open panel re-reads its counts. Default 2s.
    /// <para>
    /// Every open tab is a reader competing with the processors for SQLite's single write lock, so this is
    /// a real cost, not a free knob. Panels also stop polling after <see cref="MaxPollDuration"/>.
    /// </para>
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a panel keeps polling before it parks and offers a Resume button. Default 5 minutes.
    /// A dashboard left open on a wall display would otherwise poll forever.
    /// </summary>
    public TimeSpan MaxPollDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many rows a queue panel shows per page. Default 25.</summary>
    public int PageSize { get; set; } = 25;

    /// <summary>Whether to capture logs for the log panel. Default <c>true</c>.</summary>
    public bool CaptureLogs { get; set; } = true;

    /// <summary>
    /// How many log entries the in-memory ring buffer holds. Default 500. The buffer is bounded by count,
    /// not bytes, and is dropped entirely when the process restarts — it is a tail, not a log store.
    /// </summary>
    public int LogBufferSize { get; set; } = 500;

    /// <summary>The lowest level the log panel captures. Default <see cref="LogLevel.Information"/>.</summary>
    public LogLevel LogMinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Asks for the dashboard to be served to anyone, with no authorization. <b>Honoured in Development
    /// only</b>, where the dashboard is open already; in every other environment — and on a host that names
    /// none — it is ignored and a warning at startup says so. The panels expose job payloads, stored email
    /// bodies and log lines, so an open dashboard is close to publishing your database.
    /// <para>
    /// Outside Development the dashboard always requires the <c>RaskDashboard</c> policy: define it to say
    /// who may operate the app, or leave it undefined and the dashboard denies everyone.
    /// </para>
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }
}
