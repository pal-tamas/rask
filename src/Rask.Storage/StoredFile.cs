using Rask.Data;

namespace Rask.Storage;

/// <summary>One stored file: what it is, where its bytes are, and whether it is public.</summary>
/// <remarks>
/// A class rather than a record: a record class can only derive from another record, and <c>Id</c>/<c>CreatedAt</c>
/// come from the base. No <see cref="SoftDelete"/> and no <see cref="Versioned"/>: the orphan sweep removes
/// rows for real, and nothing edits one twice.
/// </remarks>
public sealed class StoredFile : Aggregate<Guid, Timestamps>
{
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

    /// <summary>The tenant the file was saved for, or <c>null</c> for one the host itself saved.</summary>
    /// <remarks>
    /// A <see cref="Guid"/>, the type the framework carries a tenant in. In an app whose tenant-scoped tables number their tenants
    /// (<c>PerTenant&lt;int&gt;</c>) the column is that number, and this is the <see cref="Guid"/> that carries it —
    /// the one <c>Tenant.Use(42)</c> opens.
    /// </remarks>
    public Guid? TenantId { get; private set; }

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
        file.TenantId = Tenant.Key;
        return file;
    }
}
