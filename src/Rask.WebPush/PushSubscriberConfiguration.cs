using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.WebPush;

internal sealed class PushSubscriberConfiguration : IEntityTypeConfiguration<PushSubscriber>
{
    public void Configure(EntityTypeBuilder<PushSubscriber> entity)
    {
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
        // Mapped explicitly, and the table is deliberately not PerTenant: a broadcast from a background
        // job has no tenant to filter by. The tenant is data on the row.
        entity.Property(s => s.TenantId);
    }
}
