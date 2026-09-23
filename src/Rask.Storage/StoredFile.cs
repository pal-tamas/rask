using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Storage;

/// <summary>One stored file: what it is, where its bytes are, and whether it is public.</summary>
/// <remarks>
/// A class rather than a record since it became an <see cref="Entity{TId}"/>: a record class can only derive
/// from another record, and <c>Id</c>/<c>CreatedAt</c> now come from the base. Nothing used <c>with</c> or its
/// value equality. An <see cref="Entity{TId}"/> and not an <see cref="Aggregate{TId}"/> for now — soft delete
/// is arguably wanted for files, but it would arrive with a version this row has no use for.
/// </remarks>
public sealed class StoredFile : Entity<Guid>
{
    /// <summary>No form model: a file row is written by the store after the bytes land, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    /// <summary>The display name the file was uploaded with, reduced to a safe leaf.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>The media type sniffed from the file's bytes — never the one the browser claimed.</summary>
    /// <remarks>
    /// Serve a file through <see cref="IFiles.Url"/>, <c>Files.Share(id).For(…)</c> or
    /// <see cref="IFiles.Download"/>: they send HTML, SVG and XML as downloads. Passing this type to
    /// <c>Results.File</c> yourself does not, and an uploaded page would then run on your origin.
    /// </remarks>
    public string ContentType { get; internal set; } = "";

    /// <summary>Size in bytes.</summary>
    public long Size { get; internal set; }

    /// <summary>The SHA-256 of the bytes, as lowercase hex. Also the file's HTTP entity tag.</summary>
    public string Sha256 { get; internal set; } = "";

    /// <summary>The store the bytes were written to.</summary>
    public StorageProvider Provider { get; internal set; }

    /// <summary>The object key within that store.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>Whether <see cref="IFiles.Url"/> serves the file to anyone who has the link.</summary>
    public bool Public { get; internal set; }

    /// <summary>Records a file whose bytes are already stored.</summary>
    /// <param name="id">The id the object key was built from, so the row and the bytes agree.</param>
    /// <param name="name">The safe display name.</param>
    /// <param name="contentType">The sniffed media type.</param>
    /// <param name="size">Size in bytes.</param>
    /// <param name="sha256">The SHA-256 of the bytes, lowercase hex.</param>
    /// <param name="provider">The store the bytes went to.</param>
    /// <param name="key">The object key within that store.</param>
    /// <param name="isPublic">Whether the link serves it to anyone.</param>
    /// <param name="savedAt">When the bytes landed (UTC).</param>
    internal static StoredFile For(
        Guid id,
        string name,
        string contentType,
        long size,
        string sha256,
        StorageProvider provider,
        string key,
        bool isPublic,
        DateTime savedAt)
    {
        var file = new StoredFile
        {
            Id = id,
            Name = name,
            ContentType = contentType,
            Size = size,
            Sha256 = sha256,
            Provider = provider,
            Key = key,
            Public = isPublic,
        };

        // This package can be pointed at any DbContext, Rask interceptors or not, and CreatedAt is load
        // bearing here: it is the orphan sweep's cutoff and the Last-Modified of every file response. So the
        // row records its own time rather than hoping something else will.
        file.Stamp(savedAt);

        // And which tenant it belongs to, for the same reason: the interceptors may not be there. Null when
        // there is none, which is an ordinary answer — a file saved by the host itself belongs to nobody.
        file.RecordTenant(Current.Tenant);
        return file;
    }
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

        // Mapped EXPLICITLY, and the table is deliberately not Tenancy.PerTenant. A partitioned table takes
        // a query filter, and a filter would hide other tenants' rows from the orphan sweep — which has to
        // see every file to decide what is unreferenced. The tenant is data on the row, scoped at the two
        // places a file is reached by id, and ApplyRaskConventions leaves a tenant mapped by hand alone.
        entity.Property(f => f.TenantId);
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
