namespace Rask.Background;

/// <summary>
///     The rows of the work the current user enqueued and watches, read again on every <see cref="Every" />: what
///     <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> show, with <see cref="WorkReported" /> moving them between reads.
/// </summary>
/// <remarks>
///     Rask.Jobs registers it. A subscription is one process's, so the rows are the truth: work that runs on another host,
///     or ended while nobody watched, is on the screen within one read.
/// </remarks>
internal interface IWorkBoard
{
    /// <summary>How often the rows are read again: the job processor's poll, which is how often they change.</summary>
    TimeSpan Every { get; }

    /// <summary>
    ///     The watched work the current user may see in their tenant — their own, and what the tenant shares with them
    ///     (<c>.ByTenant()</c>) — what runs or waits, and what ended lately, newest first; each row says whether they started
    ///     it (<see cref="WorkReported.Mine" />). Empty for nobody signed in.
    /// </summary>
    /// <param name="lane">One lane's work, or null for every lane.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken);
}
