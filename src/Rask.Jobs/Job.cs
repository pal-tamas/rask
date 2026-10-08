using Rask.Data;

namespace Rask.Background;

/// <summary>
/// A persisted background job awaiting (or having completed) execution. Written by <see cref="IJobs"/>
/// and drained by the <see cref="JobProcessor{TContext}"/>.
/// </summary>
/// <remarks>
/// An <see cref="Entity{TId}"/> rather than an <see cref="Aggregate{TId}"/>: an aggregate's soft-delete query
/// filter would hide rows from the claim query and turn the dashboard's purge into a stamp, and its
/// <c>Version</c> would be a second concurrency token beside <see cref="ClaimToken"/> that the processor's
/// <c>ExecuteUpdate</c> never maintains. <c>CreatedAt</c> now comes from the base — the same column it always
/// was.
/// </remarks>
public sealed class Job : Entity<long>
{
    /// <summary>No form model: a job is enqueued by code, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    private Job()
    {
    }

    /// <summary>The job's registered type name (see <see cref="JobSerializerRegistry"/>).</summary>
    public string Type { get; private set; } = "";

    /// <summary>The JSON-serialized job payload.</summary>
    public string Payload { get; private set; } = "";

    /// <summary>The earliest time (UTC) the job is eligible to run — enqueue time, or later for a delayed job or a backed-off retry.</summary>
    public DateTime RunAt { get; private set; }

    /// <summary>When the job completed successfully (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>How many times the job has been attempted.</summary>
#pragma warning disable S1144 // EF materializes the column through the setter; code only moves it with ExecuteUpdate
    public int Attempts { get; private set; }
#pragma warning restore S1144

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// The processor instance currently holding this job, or <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    /// Also the optimistic-concurrency token, which is what stops an instance whose lease expired
    /// mid-run from stamping its outcome over the row another instance has since taken.
    /// </remarks>
    public Guid? ClaimToken { get; private set; }

    /// <summary>
    /// When the current claim expires (UTC). Null or in the past means the job is claimable — which is
    /// also how a processor that died mid-job releases its work: the lease simply runs out.
    /// </summary>
    public DateTime? ClaimedUntil { get; private set; }

    /// <summary>
    /// The user the job was enqueued for — <c>Current.UserId</c> at the time — or <c>null</c> when it
    /// was enqueued for nobody.
    /// </summary>
    /// <remarks>
    /// The runner re-enters it with <c>Current.UseUser</c> before invoking the handler, so a handler
    /// reads <c>Current.UserId</c> exactly as the page that enqueued it would have. Recorded, like the tenant,
    /// because the work runs later, on another thread, with nobody signed in.
    /// </remarks>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// The tenant the job was enqueued for — <c>Current.Tenant</c> at the time — or <c>null</c> when it
    /// belongs to nobody.
    /// </summary>
    /// <remarks>
    /// Recorded as data, not as a partition: the drain sees every tenant's work, and the runner
    /// re-enters this one with <c>Tenant.Use</c> before invoking the handler.
    /// </remarks>
    public Guid? TenantId { get; private set; }

    /// <summary>Enqueues a job.</summary>
    /// <param name="type">The job's registered type name.</param>
    /// <param name="payload">The serialized job.</param>
    /// <param name="runAt">The earliest time (UTC) it may run.</param>
    /// <remarks>
    ///     <para>The key is the store's, and is the tiebreak for run order, so nothing assigns one here.</para>
    ///     <para>
    ///         The row records the tenant and the user it was enqueued for, so the runner can re-enter them
    ///         before invoking the handler. Null when there is none — a job scheduled by the host itself
    ///         belongs to nobody.
    ///     </para>
    /// </remarks>
    public static Job For(string type, string payload, DateTime runAt)
    {
        return new Job { Type = type, Payload = payload, RunAt = runAt, UserId = Current.UserId, TenantId = Current.Tenant };
    }

    /// <summary>Records a successful run, clearing any error from an earlier attempt.</summary>
    /// <param name="at">When it completed (UTC).</param>
    public void Completed(DateTime at)
    {
        ProcessedAt = at;
        Error = null;
    }

    /// <summary>Records why an attempt failed, and when the job may next be tried.</summary>
    /// <param name="error">The failure message.</param>
    /// <param name="retryAt">The earliest time (UTC) of the next attempt.</param>
    public void Failed(string error, DateTime retryAt)
    {
        Error = error;
        RunAt = retryAt;
    }

    /// <summary>Drops the claim, so another processor (or this one, later) may take the row.</summary>
    public void Release()
    {
        ClaimToken = null;
        ClaimedUntil = null;
    }
}
