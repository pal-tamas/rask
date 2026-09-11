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
/// so nothing here needs reflection. No message repeats a credential's value.
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

        Set(section["PublicBaseUrl"], v => options.PublicBaseUrl = v);
        Set(section["Prefix"], v => options.Prefix = v);
        Set(section["Disk:Root"], v => options.Disk.Root = v);

        if (section["S3:ServiceUrl"] is { Length: > 0 } serviceUrl)
        {
            options.S3.ServiceUrl = Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri)
                ? uri
                : throw new InvalidOperationException(
                    $"Storage__S3__ServiceUrl '{serviceUrl}' is not an absolute URL, like https://s3.us-east-1.amazonaws.com.");
        }

        Set(section["S3:Bucket"], v => options.S3.Bucket = v);
        Set(section["S3:Region"], v => options.S3.Region = v);
        Set(section["S3:AccessKeyId"], v => options.S3.AccessKeyId = v);
        Set(section["S3:SecretAccessKey"], v => options.S3.SecretAccessKey = v);
        Set(section["S3:SessionToken"], v => options.S3.SessionToken = v);

        if (section["S3:UsePathStyle"] is { Length: > 0 } pathStyle)
        {
            options.S3.UsePathStyle = bool.TryParse(pathStyle, out var value)
                ? value
                : throw new InvalidOperationException($"Storage__S3__UsePathStyle is '{pathStyle}'; use true or false.");
        }

        Set(section["Azure:ConnectionString"], v => options.Azure.ConnectionString = v);
        Set(section["Azure:Container"], v => options.Azure.Container = v);
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

    private static void Set(string? value, Action<string> assign)
    {
        if (value is { Length: > 0 })
        {
            assign(value);
        }
    }
}
