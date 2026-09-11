namespace Rask.Storage;

/// <summary>Where a file's bytes are kept.</summary>
public enum StorageProvider
{
    /// <summary>A directory on the server — <c>/data/files</c> on the deploy volume by default.</summary>
    Disk,
}
