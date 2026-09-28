namespace Rask.Dashboard.Panels;

/// <summary>A recurring job's schedule joined to when it actually last fired.</summary>
/// <param name="Name">The durable name.</param>
/// <param name="Schedule">When it should run, as an operator reads it: "every 1h", "daily at 03:00".</param>
/// <param name="LastEnqueuedAt">When it was last enqueued, or <c>null</c> if it never has been.</param>
public sealed record RecurringJobRow(string Name, string Schedule, DateTime? LastEnqueuedAt);
