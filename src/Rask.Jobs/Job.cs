using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Jobs;

/// <summary>
/// A persisted background job awaiting (or having completed) execution. Written by <see cref="IJobQueue"/>
/// and drained by the <see cref="JobProcessor{TContext}"/>.
/// </summary>
public sealed class Job
{
    /// <summary>Database-generated, monotonically increasing key — the tiebreak for run order.</summary>
    public long Id { get; set; }

    /// <summary>The job's registered type name (see <see cref="JobSerializerRegistry"/>).</summary>
    public string Type { get; set; } = "";

    /// <summary>The JSON-serialized job payload.</summary>
    public string Payload { get; set; } = "";

    /// <summary>The earliest time (UTC) the job is eligible to run — enqueue time, or later for a delayed job or a backed-off retry.</summary>
    public DateTime RunAt { get; set; }

    /// <summary>When the job completed successfully (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>How many times the job has been attempted.</summary>
    public int Attempts { get; set; }

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; set; }

    /// <summary>When the job was enqueued (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The processor instance currently holding this job, or <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    /// Also the optimistic-concurrency token, which is what stops an instance whose lease expired
    /// mid-run from stamping its outcome over the row another instance has since taken.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    /// <summary>
    /// When the current claim expires (UTC). Null or in the past means the job is claimable — which is
    /// also how a processor that died mid-job releases its work: the lease simply runs out.
    /// </summary>
    public DateTime? ClaimedUntil { get; set; }
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
