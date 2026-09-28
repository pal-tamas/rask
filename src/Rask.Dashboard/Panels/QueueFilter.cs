namespace Rask.Dashboard.Panels;

/// <summary>Which slice of a queue to show.</summary>
public enum QueueFilter
{
    /// <summary>Everything still outstanding — due, delayed, and dead-lettered.</summary>
    Outstanding,

    /// <summary>Eligible to run now and not yet exhausted.</summary>
    Due,

    /// <summary>Waiting on a backoff or a scheduled time.</summary>
    Delayed,

    /// <summary>Given up: out of attempts, still unprocessed. The number that matters.</summary>
    Failed,

    /// <summary>Completed.</summary>
    Processed,
}
