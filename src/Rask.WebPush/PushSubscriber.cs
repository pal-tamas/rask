using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>
/// A browser that asked for push: one row per subscription, on the app's own database.
/// </summary>
/// <remarks>
/// A browser holds one subscription per service-worker registration and a person has several browsers, so
/// <see cref="UserId" /> is a grouping, not a key — <c>Push.Send(message).To(userId)</c> reaches every device
/// that signed in. The row is written by <see cref="Push.Subscribe" /> and the <c>/_rask/push/subscribe</c>
/// endpoint, and removed when the push service says the subscription is gone (<see cref="WebPushResult.ShouldDelete" />).
/// </remarks>
public sealed class PushSubscriber : Entity<Guid>
{
    /// <summary>Written by the battery alone: no form model, no <c>CreateAsync</c>.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    /// <summary>The push service URL a message is POSTed to. Unique: a browser that subscribes again replaces its row.</summary>
    public string Endpoint { get; internal set; } = "";

    /// <summary>The browser's P-256 ECDH public key, base64url.</summary>
    public string P256dh { get; internal set; } = "";

    /// <summary>The browser's auth secret, base64url.</summary>
    public string Auth { get; internal set; } = "";

    /// <summary>The account that was signed in when the browser subscribed, when one was.</summary>
    public Guid? UserId { get; internal set; }

    /// <summary>When the push service will drop the subscription, when it said.</summary>
    public DateTime? ExpiresAt { get; internal set; }

    /// <summary>The wire shape a sender encrypts for.</summary>
    public PushSubscription Subscription => new(Endpoint, P256dh, Auth);

    internal static PushSubscriber For(PushSubscription subscription, Guid? userId, DateTime now)
    {
        var subscriber = new PushSubscriber { Id = Guid.CreateVersion7() };
        subscriber.Renew(subscription, userId, now);
        return subscriber;
    }

    internal void Renew(PushSubscription subscription, Guid? userId, DateTime now)
    {
        Endpoint = subscription.Endpoint;
        P256dh = subscription.P256dh;
        Auth = subscription.Auth;
        UserId = userId;
        ExpiresAt = subscription.ExpirationTime is { } ms
            ? DateTime.UnixEpoch.AddMilliseconds(ms)
            : null;

        // This battery can be pointed at any DbContext, Rask interceptors or not, so the row records its own
        // time and tenant rather than hoping something else will.
        Stamp(now);
        RecordTenant(Current.Tenant);
    }
}

internal sealed class PushSubscriberConfiguration : IEntityTypeConfiguration<PushSubscriber>
{
    public void Configure(EntityTypeBuilder<PushSubscriber> entity)
    {
        entity.HasKey(s => s.Id);
        entity.Property(s => s.Endpoint).HasMaxLength(2048).IsRequired();
        entity.Property(s => s.P256dh).HasMaxLength(512).IsRequired();
        entity.Property(s => s.Auth).HasMaxLength(512).IsRequired();
        entity.HasIndex(s => s.Endpoint).IsUnique();
        entity.HasIndex(s => s.UserId);
        entity.Ignore(s => s.Subscription);
        // Mapped explicitly, and the table is deliberately not Tenancy.PerTenant: a broadcast from a background
        // job has no tenant to filter by. The tenant is data on the row.
        entity.Property(s => s.TenantId);
    }
}

/// <summary>Maps the Web Push battery's table onto a context.</summary>
public static class WebPushModelBuilderExtensions
{
    /// <summary>Adds the <see cref="PushSubscriber" /> table. Call it in <c>OnModelCreating</c>, before <c>ApplyRaskConventions</c>.</summary>
    public static ModelBuilder AddRaskWebPush(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new PushSubscriberConfiguration());
        return modelBuilder;
    }
}
