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
