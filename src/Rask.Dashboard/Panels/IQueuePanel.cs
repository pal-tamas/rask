namespace Rask.Dashboard.Panels;

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
    Ui.IconName Icon { get; }

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
