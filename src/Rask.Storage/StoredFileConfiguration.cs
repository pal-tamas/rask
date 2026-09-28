using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Storage;

/// <summary>The EF Core mapping for <see cref="StoredFile"/>.</summary>
internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> entity)
    {
        entity.HasKey(f => f.Id);
        entity.Property(f => f.Name).HasMaxLength(255).IsRequired();
        entity.Property(f => f.ContentType).HasMaxLength(127).IsRequired();
        entity.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        // As a string: an integer would renumber silently if a provider were ever inserted mid-enum.
        entity.Property(f => f.Provider).HasConversion<string>().HasMaxLength(16);
        entity.Property(f => f.Key).HasMaxLength(512).IsRequired();
        // The dashboard lists newest first.
        entity.HasIndex(f => f.CreatedAt);

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes
        // a query filter, and a filter would hide other tenants' rows from the orphan sweep — which has to
        // see every file to decide what is unreferenced. The tenant is data on the row, scoped at the two
        // places a file is reached by id, and ApplyRaskConventions leaves a tenant mapped by hand alone.
        entity.Property(f => f.TenantId);
    }
}
