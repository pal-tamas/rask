using Microsoft.EntityFrameworkCore;

namespace Rask.Outbox;

/// <summary>Model-building helper for the outbox table.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>Maps the <see cref="OutboxMessage"/> table. Call from your context's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddRaskOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }
}
