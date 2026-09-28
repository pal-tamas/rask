namespace Rask.Storage;

/// <summary>Settings for <see cref="StorageProvider.Disk"/>.</summary>
public sealed class DiskStorageOptions
{
    /// <summary>
    /// The directory files are written under. Unset, it is <c>/data/files</c> when the <c>/data</c> deploy volume
    /// exists, and <c>storage/</c> under the content root otherwise. A relative path is resolved against the
    /// content root. Configuration: <c>Rask__Storage__Disk__Root</c>.
    /// </summary>
    public string? Root { get; set; }
}
