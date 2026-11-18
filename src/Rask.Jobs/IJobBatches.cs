namespace Rask.Background;

/// <summary>
///     The write under <c>Jobs.Enqueue(jobs)</c> (#1368): every row at once, all or none, stamped with one batch. Internal, so
///     <see cref="IJobs" /> keeps its shape: the real queue and the test fake both take it.
/// </summary>
internal interface IJobBatches
{
    /// <summary>Writes one row per job, in one save, and answers how many were written.</summary>
    /// <param name="jobs">Each job, with the title it is noticed under, if any.</param>
    /// <param name="at">When they may run, if at a moment.</param>
    /// <param name="after">How long from now they may run, if after a while.</param>
    /// <param name="lane">The lane they run in, or null.</param>
    /// <param name="shared">Whether their tenant shares their notices.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<int> AddAll(
        IReadOnlyList<(IJob Job, string? Notice)> jobs,
        DateTimeOffset? at,
        TimeSpan? after,
        string? lane,
        bool shared,
        CancellationToken cancellationToken);
}
