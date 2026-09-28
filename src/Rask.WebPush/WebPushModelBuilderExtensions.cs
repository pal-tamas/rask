using Microsoft.EntityFrameworkCore;

namespace Rask.WebPush;

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
