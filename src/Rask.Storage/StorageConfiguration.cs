using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Rask.Storage;

/// <summary>
/// Reads the <c>Storage</c> configuration section onto <see cref="StorageOptions"/>.
/// </summary>
/// <remarks>
/// Here, inside <c>AddRaskStorage</c>, rather than in the meta package's wiring: a scaffolded app calls
/// <c>AddRaskStorage&lt;AppDbContext&gt;()</c> directly and never passes through that wiring, so a key read there
/// would be silently ignored by every app <c>rask new</c> writes. Explicit keys, not <c>ConfigurationBinder</c>,
/// so nothing here needs reflection.
/// </remarks>
internal static class StorageConfiguration
{
    internal static void Apply(StorageOptions options, IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        var section = configuration.GetSection("Storage");

        if (section["Provider"] is { Length: > 0 } provider)
        {
            // Names only: Enum.TryParse would also accept "7".
            if (!Enum.TryParse<StorageProvider>(provider, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed)
                || int.TryParse(provider, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException(
                    $"Storage__Provider is '{provider}', which is not a storage provider. Use one of: "
                    + string.Join(", ", Enum.GetNames<StorageProvider>()) + ".");
            }

            options.Provider = parsed;
        }

        if (section["MaxFileSize"] is { Length: > 0 } maxFileSize)
        {
            if (!long.TryParse(maxFileSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            {
                throw new InvalidOperationException(
                    $"Storage__MaxFileSize is '{maxFileSize}'; it must be a whole number of bytes, like 104857600.");
            }

            options.MaxFileSize = bytes;
        }

        if (section["PublicBaseUrl"] is { Length: > 0 } publicBaseUrl)
        {
            options.PublicBaseUrl = publicBaseUrl;
        }

        if (section["Prefix"] is { Length: > 0 } prefix)
        {
            options.Prefix = prefix;
        }

        if (section["Disk:Root"] is { Length: > 0 } root)
        {
            options.Disk.Root = root;
        }
    }

    /// <summary>
    /// Settles <see cref="DiskStorageOptions.Root"/> to an absolute path: the configured one, else the deploy
    /// volume when it is mounted, else <c>storage/</c> under the content root. Creates nothing — the directory
    /// appears on the first save, so an app that never stores a file never grows one.
    /// </summary>
    internal static void ResolveDiskRoot(StorageOptions options, string contentRootPath)
    {
        if (options.Disk.Root is { Length: > 0 } root)
        {
            options.Disk.Root = Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(contentRootPath, root));
            return;
        }

        options.Disk.Root = Directory.Exists(options.DataVolume)
            ? Path.GetFullPath(Path.Combine(options.DataVolume, "files"))
            : Path.GetFullPath(Path.Combine(contentRootPath, "storage"));
    }
}
