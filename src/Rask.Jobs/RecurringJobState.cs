using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Jobs;

/// <summary>
/// Durable bookkeeping for an interval-recurring job, keyed by its registered name, so a restart never
/// double-enqueues within an interval (and enqueues a single catch-up run if the app was down past the due time).
/// </summary>
/// <remarks>
/// The table carries a surrogate <c>Id</c> and keeps <see cref="Name"/> as a UNIQUE index. The uniqueness is
/// what the create race relies on — the instance that loses gets a constraint violation either way — so
/// nothing about the scheduling changes; the surrogate is only what lets the row have a read face.
/// </remarks>
public sealed class RecurringJobState : Entity<Guid>
{
    /// <summary>No form model: this row is scheduler bookkeeping, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    private RecurringJobState()
    {
    }

    /// <summary>The recurring job's registered name (see <see cref="RecurringJob.Name"/>).</summary>
    public string Name { get; private set; } = "";

    /// <summary>When this recurring job was last enqueued (UTC), or <c>null</c> if it never has been.</summary>
    public DateTime? LastEnqueuedAt { get; private set; }

    /// <summary>Starts the bookkeeping for <paramref name="name" />, never yet enqueued.</summary>
    /// <param name="name">The recurring job's registered name.</param>
    public static RecurringJobState For(string name) =>
        new() { Id = Guid.CreateVersion7(), Name = name };
}

/// <summary>The EF Core mapping for <see cref="RecurringJobState"/>.</summary>
public sealed class RecurringJobStateConfiguration : IEntityTypeConfiguration<RecurringJobState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RecurringJobState> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
        // The name is still what identifies a recurring job, and still what makes two instances racing to
        // create the row resolve to one winner — as a unique index rather than as the primary key.
        entity.HasIndex(x => x.Name).IsUnique();
    }
}
