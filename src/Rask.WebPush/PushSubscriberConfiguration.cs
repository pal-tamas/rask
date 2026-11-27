using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.WebPush;

internal sealed class PushSubscriberConfiguration : IEntityTypeConfiguration<PushSubscriber>
{
    public void Configure(EntityTypeBuilder<PushSubscriber> entity)
    {
        // A battery's table keeps its names whatever the app's TableNames and ColumnNames say.
        KeptNames.Keep(entity.Metadata.ClrType);

        // Not versioned and never soft-deleted, mapped with or without ApplyRaskConventions.
        entity.Ignore(s => s.Version);
        entity.Ignore(s => s.DeletedAt);
        entity.HasKey(s => s.Id);
        entity.Property(s => s.Endpoint).HasMaxLength(2048).IsRequired();
        entity.Property(s => s.P256dh).HasMaxLength(512).IsRequired();
        entity.Property(s => s.Auth).HasMaxLength(512).IsRequired();
        entity.HasIndex(s => s.Endpoint).IsUnique();
        entity.HasIndex(s => s.UserId);
        entity.Ignore(s => s.Subscription);
        // Rask's own moment: stored as the UTC instant it holds, on any context.
        TimestampColumn.Map(entity.Property(s => s.ExpiresAt));
        // Mapped explicitly, and the table is deliberately not IBelongsTo<Tenant>: a broadcast from a background
        // job has no tenant to filter by. The tenant is data on the row.
        entity.Property(s => s.TenantId);
    }
}
