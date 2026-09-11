using System.Globalization;

namespace Rask.Storage;

/// <summary>
/// Where files go and what is accepted. Every option can also come from configuration under <c>Storage</c>
/// (<c>Storage__Provider</c>, <c>Storage__MaxFileSize</c>, <c>Storage__Disk__Root</c>, …); code set here wins
/// over configuration.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>The default <see cref="MaxFileSize"/>: 50 MB, the same as the server's upload limit.</summary>
    public const long DefaultMaxFileSize = 50 * 1024 * 1024;

    /// <summary>Where the bytes go. Default <see cref="StorageProvider.Disk"/>. Configuration: <c>Storage__Provider</c>.</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Disk;

    /// <summary>
    /// The largest file accepted, in bytes. Default 50 MB. Configuration: <c>Storage__MaxFileSize</c>. The
    /// transport has its own limit too — raise the server's upload limit alongside this one.
    /// </summary>
    public long MaxFileSize { get; set; } = DefaultMaxFileSize;

    /// <summary>
    /// The media types accepted, as <c>type/subtype</c> or <c>type/*</c> — matched against the type sniffed
    /// from the bytes. Empty (the default) accepts anything.
    /// </summary>
    public IList<string> AllowedTypes { get; } = [];

    /// <summary>
    /// An absolute URL that public files are served from — a CDN or a public bucket domain — which
    /// <see cref="IFiles.Url"/> joins with the file's key. Unset, the app serves them itself.
    /// Configuration: <c>Storage__PublicBaseUrl</c>. Prefer an origin other than the app's own.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// A key prefix, so one bucket can hold several apps (<c>"myapp/"</c>). Default empty. The orphan sweep
    /// only ever looks under it. Configuration: <c>Storage__Prefix</c>.
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// How old bytes with no row must be before the sweep removes them. Default 24 hours, at least 5
    /// minutes — longer than any save could still be in flight.
    /// </summary>
    public TimeSpan OrphanGracePeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How often the orphan sweep runs. Default 24 hours.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The <see cref="StorageProvider.Disk"/> settings.</summary>
    public DiskStorageOptions Disk { get; } = new();

    /// <summary>The deploy volume <c>rask deploy</c> mounts; a seam so tests never write to a real one.</summary>
    internal string DataVolume { get; set; } = "/data";

    internal const string MinimumGracePeriodText = "5 minutes";

    /// <summary>Checks the options and normalizes the ones with a canonical form. Throws naming what to change.</summary>
    internal void Validate(string? webRootPath)
    {
        if (MaxFileSize <= 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"StorageOptions.MaxFileSize is {MaxFileSize}; it must be a positive number of bytes (Storage__MaxFileSize)."));
        }

        if (OrphanGracePeriod < TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"StorageOptions.OrphanGracePeriod must be at least {MinimumGracePeriodText}, so the sweep can never remove a file whose save is still in flight.");
        }

        // PeriodicTimer rejects an interval above (uint.MaxValue - 1) ms (~49.7 days).
        if (SweepInterval <= TimeSpan.Zero || SweepInterval.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new InvalidOperationException("StorageOptions.SweepInterval must be positive and at most 49 days.");
        }

        foreach (var type in AllowedTypes)
        {
            if (!IsMediaTypePattern(type))
            {
                throw new InvalidOperationException(
                    $"StorageOptions.AllowedTypes contains '{type}', which is not a media type. Name types, not extensions: "
                    + "o.AllowedTypes.Add(\"application/pdf\"), or a family: o.AllowedTypes.Add(\"image/*\").");
            }
        }

        Prefix = NormalizePrefix(Prefix);
        PublicBaseUrl = NormalizePublicBaseUrl(PublicBaseUrl);

        if (Provider == StorageProvider.Disk)
        {
            ValidateDiskRoot(webRootPath);
        }
    }

    private void ValidateDiskRoot(string? webRootPath)
    {
        var root = Disk.Root ?? throw new InvalidOperationException("The disk root was not resolved.");

        if (string.IsNullOrEmpty(webRootPath))
        {
            return;
        }

        // Anything under wwwroot is served by the static-file middleware with none of the checks the file
        // routes apply — an uploaded HTML page would be served as HTML, from the app's own origin.
        var web = Path.TrimEndingDirectorySeparator(Path.GetFullPath(webRootPath)) + Path.DirectorySeparatorChar;
        var files = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (files.StartsWith(web, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The storage directory '{root}' is inside the web root '{webRootPath}', where the static-file middleware "
                + "would serve uploads with none of Rask.Storage's checks. Point Storage__Disk__Root outside wwwroot.");
        }
    }

    private static string NormalizePrefix(string? prefix)
    {
        var trimmed = (prefix ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        if (!KeyLayout.IsValidPrefix(trimmed))
        {
            throw new InvalidOperationException(
                $"StorageOptions.Prefix '{prefix}' can only use lowercase letters, digits, '.', '_', '-' and '/' between "
                + "non-empty segments, like o.Prefix = \"myapp/\" (Storage__Prefix).");
        }

        return trimmed;
    }

    private static string? NormalizePublicBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"StorageOptions.PublicBaseUrl '{value}' must be an absolute https URL with no query or fragment, like "
                + "o.PublicBaseUrl = \"https://files.example.com/\" (Storage__PublicBaseUrl). http is accepted only for localhost.");
        }

        var text = uri.GetLeftPart(UriPartial.Path);
        return text.EndsWith('/') ? text : text + "/";
    }

    private static bool IsMediaTypePattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        var type = value.AsSpan(0, slash);
        var subtype = value.AsSpan(slash + 1);
        return IsToken(type) && (subtype is "*" || IsToken(subtype));
    }

    private static bool IsToken(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '&' or '^' or '_' or '.' or '+' or '-'))
            {
                return false;
            }
        }

        return !value.IsEmpty;
    }
}

/// <summary>Settings for <see cref="StorageProvider.Disk"/>.</summary>
public sealed class DiskStorageOptions
{
    /// <summary>
    /// The directory files are written under. Unset, it is <c>/data/files</c> when the <c>/data</c> deploy volume
    /// exists, and <c>storage/</c> under the content root otherwise. A relative path is resolved against the
    /// content root. Configuration: <c>Storage__Disk__Root</c>.
    /// </summary>
    public string? Root { get; set; }
}
