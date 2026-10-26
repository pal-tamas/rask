using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Background;

/// <summary>The EF Core mapping for <see cref="Job"/>.</summary>
public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Not versioned and never soft-deleted, mapped with or without ApplyRaskConventions.
        builder.Ignore(x => x.Version);
        builder.Ignore(x => x.DeletedAt);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Payload).IsRequired();

        // Unbounded on purpose, and SAID so: an app-wide string length (IRaskModel's Conventions) applies to every column
        // that states none, and would otherwise cut these off.
        foreach (var text in new[] { nameof(Job.Payload), nameof(Job.Error), nameof(Job.UserClaims) })
        {
            builder.Property(text).Metadata.SetMaxLength(null);
        }

        // Drives the "due, oldest first" claim query. ClaimedUntil is deliberately NOT in the index: in a
        // healthy queue almost every candidate row is unclaimed, so it costs nothing as a residual filter,
        // and a filtered index would need provider-specific SQL. The claim's read-back rides the primary
        // key (it filters on the same id list), so ClaimToken needs no index either.
        builder.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
        // Fences the completion write: EF appends `AND ClaimToken = @original` to every tracked update, so
        // an instance whose lease expired mid-job gets a concurrency exception instead of overwriting the
        // outcome of whichever instance now owns the row.
        builder.Property(x => x.ClaimToken).IsConcurrencyToken();

        // Mapped EXPLICITLY, and the table is deliberately not PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the runner has to
        // see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        builder.Property(x => x.TenantId);

        // The lane is data on the row, and the claim looks a lane up to see whether it is busy.
        builder.Property(x => x.Lane).HasMaxLength(256);
        builder.HasIndex(x => new { x.Lane, x.ProcessedAt });

        // What refuses a second copy of work that already waits (.UnlessWaiting()), across every host at once.
        // Only a waiting, never-claimed row holds a key — the claim clears it — so no clock is in the predicate.
        // NULLs never collide: SQLite and PostgreSQL treat them as distinct, and SQL Server's provider filters a
        // unique index on a nullable column to `[WaitingKey] IS NOT NULL`, so there it indexes waiting rows alone.
        builder.Property(x => x.WaitingKey).HasMaxLength(JobWaitingKey.Length);
        builder.HasIndex(x => x.WaitingKey).IsUnique();
    }
}
