using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Rask.Storage;

/// <summary>
/// Binds the <c>Rask:Storage</c> configuration section onto <see cref="StorageOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Here, inside <c>AddRaskStorage</c>, rather than in the meta package's wiring: a scaffolded app calls
/// <c>AddRaskStorage&lt;AppDbContext&gt;()</c> directly and never passes through that wiring, so a key read there
/// would be silently ignored by every app <c>rask new</c> writes.
/// </para>
/// <para>
/// The bind itself is the configuration binding source generator's, so nothing here needs reflection. What this adds
/// in front of it is the handful of checks whose failure the binder would report badly or not at all: it accepts
/// <c>"7"</c> for an enum, and it names neither the key nor the shape a bad number or URL should have. No message
/// repeats a credential's value.
/// </para>
/// </remarks>
internal static class StorageConfiguration
{
    /// <summary>The section every storage setting lives under (#1080), like every other Rask area.</summary>
    internal const string SectionName = "Rask:Storage";

    /// <summary>Binds the section of <paramref name="configuration"/> that storage reads. For tests and tooling.</summary>
    internal static void Apply(StorageOptions options, IConfiguration? configuration)
    {
        if (configuration is not null)
        {
            Bind(configuration.GetSection(SectionName), options);
        }
    }

    /// <summary>Checks the values the binder would misread, then binds <paramref name="section"/> onto <paramref name="options"/>.</summary>
    internal static void Bind(IConfigurationSection section, StorageOptions options)
    {
        if (section["Provider"] is { Length: > 0 } provider
            && (!Enum.TryParse<StorageProvider>(provider, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed)
                || int.TryParse(provider, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            // Names only: the binder would also accept "7", and a value that is no provider at all would surface as
            // a conversion failure that lists nothing to choose from.
            throw new InvalidOperationException(
                $"Rask__Storage__Provider is '{provider}', which is not a storage provider. Use one of: "
                + string.Join(", ", Enum.GetNames<StorageProvider>()) + ".");
        }

        if (section["MaxFileSize"] is { Length: > 0 } maxFileSize
            && !long.TryParse(maxFileSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException(
                $"Rask__Storage__MaxFileSize is '{maxFileSize}'; it must be a whole number of bytes, like 104857600.");
        }

        if (section["S3:ServiceUrl"] is { Length: > 0 } serviceUrl && !Uri.TryCreate(serviceUrl, UriKind.Absolute, out _))
        {
            // The binder would turn this into a relative Uri without a word.
            throw new InvalidOperationException(
                $"Rask__Storage__S3__ServiceUrl '{serviceUrl}' is not an absolute URL, like https://s3.us-east-1.amazonaws.com.");
        }

        if (section["S3:UsePathStyle"] is { Length: > 0 } pathStyle && !bool.TryParse(pathStyle, out _))
        {
            throw new InvalidOperationException($"Rask__Storage__S3__UsePathStyle is '{pathStyle}'; use true or false.");
        }

        section.Bind(options);
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
