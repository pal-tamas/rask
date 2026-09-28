using System.Formats.Tar;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Finds the directory Rask.Storage's disk provider keeps an app's uploads in, by reading the settings the app reads.
/// </summary>
/// <remarks>
/// Mirrors <c>StorageConfiguration.ResolveDiskRoot</c>: <c>Rask:Storage:Disk:Root</c> when set (relative to the
/// content root), else <c>files</c> on the deploy volume when <c>/data</c> exists, else <c>storage/</c> under the
/// content root. Returns null when the app is configured for S3 or Azure — those have no files on this machine.
/// </remarks>
internal static class StorageRootLocator
{
    /// <summary>The deploy volume a Rask container mounts; the storage default when it exists.</summary>
    internal const string DataVolume = "/data";

    internal static string? Locate(IFileSystem fileSystem, string projectDirectory, string? environment = null)
    {
        var provider = AppSettingsReader.ReadStrings(fileSystem, projectDirectory, environment, "Rask", "Storage", "Provider")
            .FirstOrDefault();
        if (provider is not null && !provider.Equals("Disk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (AppSettingsReader.ReadStrings(fileSystem, projectDirectory, environment, "Rask", "Storage", "Disk", "Root")
                .FirstOrDefault(root => root.Length > 0) is { } configured)
        {
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(projectDirectory, configured));
        }

        return fileSystem.DirectoryExists(DataVolume)
            ? Path.GetFullPath(Path.Combine(DataVolume, "files"))
            : Path.GetFullPath(Path.Combine(projectDirectory, "storage"));
    }
}
