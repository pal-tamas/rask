namespace Rask.Storage;

/// <summary>Where a file's bytes are kept.</summary>
public enum StorageProvider
{
    /// <summary>A directory on the server — <c>/data/files</c> on the deploy volume by default.</summary>
    Disk,

    /// <summary>
    /// An S3-compatible bucket: Amazon S3, Cloudflare R2, Backblaze B2, MinIO, DigitalOcean Spaces, or Google
    /// Cloud Storage through its S3 interoperability keys.
    /// </summary>
    S3,

    /// <summary>An Azure Blob Storage container.</summary>
    Azure,
}
