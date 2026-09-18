using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Jobs;

/// <summary>
/// A persisted background job awaiting (or having completed) execution. Written by <see cref="IJob"/>
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
    public int Attempts { get; private set; }

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

    /// <summary>Enqueues a job.</summary>
    /// <param name="type">The job's registered type name.</param>
    /// <param name="payload">The serialized job.</param>
    /// <param name="runAt">The earliest time (UTC) it may run.</param>
    /// <remarks>The key is the store's, and is the tiebreak for run order, so nothing assigns one here.</remarks>
    public static Job For(string type, string payload, DateTime runAt) =>
        new() { Type = type, Payload = payload, RunAt = runAt };

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

/// <summary>The EF Core mapping for <see cref="Job"/>.</summary>
public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Job> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Type).IsRequired().HasMaxLength(512);
        entity.Property(x => x.Payload).IsRequired();
        // Drives the "due, oldest first" claim query. ClaimedUntil is deliberately NOT in the index: in a
        // healthy queue almost every candidate row is unclaimed, so it costs nothing as a residual filter,
        // and a filtered index would need provider-specific SQL. The claim's read-back rides the primary
        // key (it filters on the same id list), so ClaimToken needs no index either.
        entity.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
        // Fences the completion write: EF appends `AND ClaimToken = @original` to every tracked update, so
        // an instance whose lease expired mid-job gets a concurrency exception instead of overwriting the
        // outcome of whichever instance now owns the row.
        entity.Property(x => x.ClaimToken).IsConcurrencyToken();
    }
}

/// <summary>Model-building helper for the jobs tables.</summary>
public static class JobsModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="Job"/> and <see cref="RecurringJobState"/> tables. Call from your context's
    /// <c>OnModelCreating</c>, then create the schema with <c>rask db add AddJobs &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskJobs(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobStateConfiguration());
        return modelBuilder;
    }
}
