using Microsoft.EntityFrameworkCore;

namespace Rask.Dashboard.Panels;

/// <summary>
/// One row of a queue table, projected to the shape the outbox, jobs and mail tables share. Keeping the
/// panel generic over this rather than over the three entity types is what lets a single page serve all
/// three — they differ only in which columns exist, not in what an operator needs to see.
/// </summary>
/// <param name="Id">The row's key.</param>
/// <param name="Type">The registered type name (or, for mail, the subject).</param>
/// <param name="Summary">A one-line description — the recipient list for mail, the payload preview otherwise.</param>
/// <param name="CreatedAt">When the row was enqueued (UTC).</param>
/// <param name="RunAt">When it next becomes eligible (UTC). Equal to <paramref name="CreatedAt"/> for the outbox, which has no delay.</param>
/// <param name="ProcessedAt">When it completed (UTC), or <c>null</c> while outstanding.</param>
/// <param name="Attempts">How many times it has been tried.</param>
/// <param name="Error">The last failure message.</param>
/// <param name="Payload">The stored payload, for the drill-down.</param>
public sealed record QueueRow(
    long Id,
    string Type,
    string Summary,
    DateTime CreatedAt,
    DateTime RunAt,
    DateTime? ProcessedAt,
    int Attempts,
    string? Error,
    string Payload);

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

/// <summary>
/// The counts an operator reads at a glance. <see cref="Failed"/> is the headline: processed climbing is
/// normal, but a system that retries itself to death still reports a healthy processed count.
/// </summary>
/// <param name="Due">Eligible to run now.</param>
/// <param name="Delayed">Waiting on a backoff or a scheduled time.</param>
/// <param name="Failed">Out of attempts and still unprocessed — dead letters.</param>
/// <param name="Processed">Completed.</param>
public readonly record struct QueueCounts(int Due, int Delayed, int Failed, int Processed)
{
    /// <summary>Everything not yet processed, whatever the reason.</summary>
    public int Outstanding => Due + Delayed + Failed;
}

/// <summary>
/// A queue the dashboard can show and act on. Implemented once per battery; the page never names an
/// entity type.
/// </summary>
public interface IQueuePanel : IQueueActions
{
    /// <summary>URL-safe identity, e.g. <c>jobs</c>. Also the route segment.</summary>
    string Slug { get; }

    /// <summary>Display name, e.g. "Jobs".</summary>
    string Title { get; }

    /// <summary>The icon for the nav entry and the overview tile.</summary>
    UiIconName Icon { get; }

    /// <summary>
    /// <c>false</c> when this battery isn't part of the app — either not registered, or registered without
    /// its table mapped into the model. The dashboard hides the panel entirely rather than showing an
    /// empty one, so what you see is what the app actually runs.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The attempt count at which this queue gives up — needed to tell a retry from a dead letter.</summary>
    int MaxAttempts { get; }

    /// <summary>The five counts, in one round-trip per count.</summary>
    Task<QueueCounts> CountsAsync(CancellationToken cancellationToken);

    /// <summary>One page of rows, newest activity first, plus the total behind it for the pager.</summary>
    Task<(IReadOnlyList<QueueRow> Rows, int Total)> PageAsync(
        QueueFilter filter, int skip, int take, CancellationToken cancellationToken);
}
