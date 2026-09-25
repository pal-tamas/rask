using Microsoft.EntityFrameworkCore;

namespace Rask.Storage;

/// <summary>Model-building helper for the stored-file table.</summary>
public static class StorageModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="StoredFile"/> table. Call from your context's <c>OnModelCreating</c>, then create the
    /// schema with <c>rask db add AddStorage &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskStorage(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new StoredFileConfiguration());
        return modelBuilder;
    }
}
