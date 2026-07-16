using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Jobs;

/// <summary>
/// Durable bookkeeping for an interval-recurring job, keyed by its registered name, so a restart never
/// double-enqueues within an interval (and enqueues a single catch-up run if the app was down past the due time).
/// </summary>
public sealed class RecurringJobState
{
    /// <summary>The recurring job's registered name (see <see cref="JobOptions.AddRecurring{TJob}"/>).</summary>
    public string Name { get; set; } = "";

    /// <summary>When this recurring job was last enqueued (UTC), or <c>null</c> if it never has been.</summary>
    public DateTime? LastEnqueuedAt { get; set; }
}

/// <summary>The EF Core mapping for <see cref="RecurringJobState"/>.</summary>
public sealed class RecurringJobStateConfiguration : IEntityTypeConfiguration<RecurringJobState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RecurringJobState> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Name);
        entity.Property(x => x.Name).HasMaxLength(200);
    }
}
