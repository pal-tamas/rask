using Microsoft.Extensions.Logging;

namespace Rask.Dashboard;

/// <summary>
/// What the dashboard is allowed to do beyond looking. Actions are opt-in by tier because the dashboard
/// sits over the same tables the processors are draining.
/// </summary>
[Flags]
public enum RaskDashboardActions
{
    /// <summary>Read-only. No button on any panel mutates anything.</summary>
    None = 0,

    /// <summary>
    /// Retry a dead letter (and retry-all), purge processed rows, evict a cache key, take a snapshot now.
    /// Each is scoped by a predicate that excludes rows a processor could currently hold, so none of them
    /// races the drain. This is the default.
    /// </summary>
    Safe = 1,

    /// <summary>
    /// Delete a row, cancel a pending job, flush the whole cache, force a recurring job to run now. These
    /// destroy work rather than reschedule it, so they stay off unless you ask for them.
    /// </summary>
    Destructive = 2,

    /// <summary>Everything.</summary>
    All = Safe | Destructive,
}

/// <summary>Options for the batteries dashboard.</summary>
public sealed class RaskDashboardOptions
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
    /// Serve the dashboard to anyone, with no authorization, in every environment. Default <c>false</c>,
    /// and there is no reason to change it on a reachable host: the panels expose job payloads, stored
    /// email bodies and log lines, so an open dashboard is close to publishing your database. It exists
    /// only so a deliberately unauthenticated internal tool doesn't have to invent a dummy policy.
    /// <para>
    /// Leaving this <c>false</c> and defining no <c>RaskDashboard</c> policy is the safe path: the
    /// dashboard denies everyone outside Development rather than opening.
    /// </para>
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }

    /// <summary>Validates the option values at registration, so a bad value fails fast.</summary>
    internal void Validate()
    {
        if (RefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RefreshInterval), RefreshInterval, "RefreshInterval must be positive.");
        }

        if (MaxPollDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPollDuration), MaxPollDuration, "MaxPollDuration cannot be negative.");
        }

        if (PageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "PageSize must be at least 1.");
        }

        if (LogBufferSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LogBufferSize), LogBufferSize, "LogBufferSize must be at least 1.");
        }
    }
}
