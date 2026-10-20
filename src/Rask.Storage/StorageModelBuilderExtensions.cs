using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Storage;

/// <summary>Model-building helper for the stored-file tables.</summary>
public static class StorageModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="StoredFile"/> table, and the <c>StoredFileBytes</c> table that
    /// <see cref="StorageProvider.Database"/> keeps the bytes in. Call from your context's <c>OnModelCreating</c>, then
    /// create the schema with <c>rask db add AddStorage &amp;&amp; rask db update</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="StoredFile.TenantId"/> is kept in the column type the app's tenant-scoped tables use: an <c>int</c>
    /// when every one of them keeps a number (<c>PerTenant&lt;int&gt;</c>), a <c>long</c> likewise, and a
    /// <c>Guid</c> otherwise. Call it before or after <c>ApplyRaskConventions</c>: either order gives the same model.
    /// </remarks>
    public static ModelBuilder AddRaskStorage(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new StoredFileConfiguration());
        modelBuilder.ApplyConfiguration(new StoredFileBytesConfiguration());
        TenantColumnFollowers.Follow(modelBuilder, typeof(StoredFile), Columns.TenantId);
        return modelBuilder;
    }
}
