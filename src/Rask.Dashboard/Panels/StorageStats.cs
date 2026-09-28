using Rask.Storage;

namespace Rask.Dashboard.Panels;

/// <summary>Storage totals for the Storage tab.</summary>
/// <param name="Files">Rows in the stored-file table.</param>
/// <param name="Bytes">Total size of the stored files.</param>
/// <param name="Public">How many are served to anyone with the link.</param>
/// <param name="ByProvider">Files and bytes per provider, largest first.</param>
/// <param name="ActiveProvider">The provider new files are written to.</param>
/// <param name="SweepInterval">How often the orphan sweep runs.</param>
/// <param name="OrphanGracePeriod">How old unreferenced bytes must be before the sweep removes them.</param>
public readonly record struct StorageStats(
    int Files,
    long Bytes,
    int Public,
    IReadOnlyList<ProviderUsage> ByProvider,
    StorageProvider ActiveProvider,
    TimeSpan SweepInterval,
    TimeSpan OrphanGracePeriod)
{
    /// <summary>No files, and nothing known about the configuration.</summary>
    public static StorageStats Empty { get; } = new(0, 0, 0, [], StorageProvider.Disk, TimeSpan.Zero, TimeSpan.Zero);
}
