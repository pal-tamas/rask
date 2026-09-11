using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Storage;

/// <summary>One stored file: what it is, where its bytes are, and whether it is public.</summary>
public sealed record StoredFile
{
    /// <summary>The file's id — keep it on your own entity.</summary>
    public Guid Id { get; init; }

    /// <summary>The display name the file was uploaded with, reduced to a safe leaf.</summary>
    public string Name { get; init; } = "";

    /// <summary>The media type sniffed from the file's bytes — never the one the browser claimed.</summary>
    /// <remarks>
    /// Serve a file through <see cref="IFiles.Url"/>, <see cref="IFiles.TemporaryUrlAsync"/> or
    /// <see cref="IFiles.Download"/>: they send HTML, SVG and XML as downloads. Passing this type to
    /// <c>Results.File</c> yourself does not, and an uploaded page would then run on your origin.
    /// </remarks>
    public string ContentType { get; init; } = "";

    /// <summary>Size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The SHA-256 of the bytes, as lowercase hex. Also the file's HTTP entity tag.</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>The store the bytes were written to.</summary>
    public StorageProvider Provider { get; init; }

    /// <summary>The object key within that store.</summary>
    public string Key { get; init; } = "";

    /// <summary>Whether <see cref="IFiles.Url"/> serves the file to anyone who has the link.</summary>
    public bool Public { get; init; }

    /// <summary>When the file was saved (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

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
    }
}

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
