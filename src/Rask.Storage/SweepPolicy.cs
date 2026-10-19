namespace Rask.Storage;

/// <summary>When the sweep is allowed to delete at all.</summary>
internal static class SweepPolicy
{
    /// <summary>
    /// A disk root and the app's own database belong to the app. A bucket is easily shared by two environments, and
    /// without a prefix a key's shape alone cannot tell this app's orphans from another app's files — so there it only
    /// reports.
    /// </summary>
    internal static bool MayDelete(StorageProvider provider, string prefix) =>
        provider is StorageProvider.Disk or StorageProvider.Database || prefix.Length > 0;
}
