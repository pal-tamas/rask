using System.Globalization;
using Rask.Storage.Backends;

namespace Rask.Storage;

/// <summary>
/// Where files go and what is accepted. The provider and its settings, the size limit, the prefix and the public
/// base URL can also come from configuration under <c>Storage</c> (<c>Storage__Provider</c>,
/// <c>Storage__MaxFileSize</c>, <c>Storage__S3__Bucket</c>, …); code set here wins over configuration.
/// <see cref="AllowedTypes"/> and the sweep's timings are set in code.
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
    /// <para>
    /// Grant public read on <c>{Prefix}public/</c> only. Private files are kept under <c>{Prefix}private/</c>, and a
    /// private file's key is visible in its temporary URL — a policy covering both would make that link permanent.
    /// </para>
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// A key prefix, so one bucket can hold several apps (<c>"myapp/"</c>). Default empty. The orphan sweep
    /// only ever looks under it — and on S3 or Azure it deletes nothing until one is set, because a key's shape
    /// alone cannot tell this app's orphans from another environment's files. Configuration: <c>Storage__Prefix</c>.
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

    /// <summary>The <see cref="StorageProvider.S3"/> settings.</summary>
    public S3StorageOptions S3 { get; } = new();

    /// <summary>The <see cref="StorageProvider.Azure"/> settings.</summary>
    public AzureStorageOptions Azure { get; } = new();

    /// <summary>The deploy volume <c>rask deploy</c> mounts; a seam so tests never write to a real one.</summary>
    internal string DataVolume { get; set; } = "/data";

    internal const string MinimumGracePeriodText = "5 minutes";

    /// <summary>The largest object one write to <paramref name="provider"/> can create.</summary>
    internal static long MaxSinglePutBytes(StorageProvider provider) => provider switch
    {
        StorageProvider.S3 => S3StorageOptions.MaxSinglePutBytes,
        StorageProvider.Azure => AzureStorageOptions.MaxSinglePutBytes,
        _ => long.MaxValue,
    };

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

        switch (Provider)
        {
            case StorageProvider.Disk:
                ValidateDiskRoot(webRootPath);
                break;
            case StorageProvider.S3:
                S3.Validate();
                break;
            case StorageProvider.Azure:
                Azure.Validate();
                break;
            default:
                throw new InvalidOperationException(
                    $"StorageOptions.Provider is {Provider}, which is not a storage provider. Use one of: "
                    + string.Join(", ", Enum.GetNames<StorageProvider>()) + ".");
        }

        var singlePut = MaxSinglePutBytes(Provider);
        if (MaxFileSize > singlePut)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"StorageOptions.MaxFileSize is {MaxFileSize} bytes, but one upload to {Provider} is limited to {singlePut} "
                + $"and multipart upload is not supported. Lower it with o.MaxFileSize, or Storage__MaxFileSize."));
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

/// <summary>
/// Settings for <see cref="StorageProvider.S3"/> — Amazon S3 and every store that speaks its API. Requests are
/// signed in-process (Signature Version 4); no cloud SDK is involved.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>One PUT creates at most 5 GiB.</summary>
    internal const long MaxSinglePutBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// The service endpoint: <c>https://s3.us-east-1.amazonaws.com</c>,
    /// <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>, <c>https://s3.us-west-004.backblazeb2.com</c>,
    /// <c>https://storage.googleapis.com</c>, or a MinIO address. Configuration: <c>Storage__S3__ServiceUrl</c>.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>The bucket. Configuration: <c>Storage__S3__Bucket</c>.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>The signing region. Default <c>us-east-1</c>; Cloudflare R2 wants <c>auto</c>. Configuration: <c>Storage__S3__Region</c>.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>The access key id. Configuration: <c>Storage__S3__AccessKeyId</c>.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>The secret access key. Configuration: <c>Storage__S3__SecretAccessKey</c> — pass it as a secret.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>An STS session token that pairs with a temporary access key. Configuration: <c>Storage__S3__SessionToken</c>.</summary>
    public string? SessionToken { get; set; }

    /// <summary>
    /// Whether the bucket is a path segment (<c>host/bucket/key</c>) rather than a subdomain
    /// (<c>bucket.host/key</c>). Default <c>true</c>, which R2, MinIO and most compatible stores require.
    /// Configuration: <c>Storage__S3__UsePathStyle</c>.
    /// </summary>
    public bool UsePathStyle { get; set; } = true;

    internal void Validate()
    {
        if (ServiceUrl is null || !ServiceUrl.IsAbsoluteUri || ServiceUrl.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "Storage__S3__ServiceUrl is required when Storage__Provider is S3: an absolute URL like "
                + "https://s3.us-east-1.amazonaws.com, https://<account>.r2.cloudflarestorage.com or http://localhost:9000.");
        }

        if (!IsBucketName(Bucket))
        {
            throw new InvalidOperationException(
                $"Storage__S3__Bucket '{Bucket}' is not a bucket name: 3 to 63 lowercase letters, digits, '.' and '-', "
                + "starting and ending with a letter or digit.");
        }

        if (string.IsNullOrWhiteSpace(Region))
        {
            throw new InvalidOperationException("Storage__S3__Region is required, like us-east-1 (Cloudflare R2: auto).");
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            throw new InvalidOperationException(
                "Storage__S3__AccessKeyId and Storage__S3__SecretAccessKey are both required when Storage__Provider is S3. "
                + "Pass the secret with rask deploy --env, never in appsettings.json.");
        }

        // A bucket with dots under a wildcard certificate: bucket.name.s3.example.com matches no *.s3.example.com.
        if (!UsePathStyle && ServiceUrl.Scheme == "https" && Bucket.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The bucket '{Bucket}' contains dots, which breaks TLS in virtual-host addressing. Set Storage__S3__UsePathStyle to true.");
        }
    }

    private static bool IsBucketName(string? name)
    {
        if (name is not { Length: >= 3 and <= 63 } || !char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Settings for <see cref="StorageProvider.Azure"/>. Requests are signed in-process (Shared Key, service SAS);
/// no cloud SDK is involved.
/// </summary>
public sealed class AzureStorageOptions
{
    /// <summary>One Put Blob creates at most 5000 MiB.</summary>
    internal const long MaxSinglePutBytes = 5000L * 1024 * 1024;

    /// <summary>
    /// The storage account's connection string. With <c>AccountName</c> and <c>AccountKey</c>, temporary URLs are
    /// signed by Azure itself and downloads never pass through the app; with <c>BlobEndpoint</c> and
    /// <c>SharedAccessSignature</c> only, the app serves them. <c>UseDevelopmentStorage=true</c> targets Azurite.
    /// Configuration: <c>Storage__Azure__ConnectionString</c> — pass it as a secret.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>The container. Configuration: <c>Storage__Azure__Container</c>.</summary>
    public string Container { get; set; } = "";

    internal void Validate()
    {
        // Parsing is the validation: it names the part that is wrong and never repeats the value.
        _ = AzureAccount.Parse(ConnectionString);

        if (!IsContainerName(Container))
        {
            throw new InvalidOperationException(
                $"Storage__Azure__Container '{Container}' is not a container name: 3 to 63 lowercase letters, digits and "
                + "single hyphens, starting and ending with a letter or digit.");
        }
    }

    private static bool IsContainerName(string? name)
    {
        if (name is not { Length: >= 3 and <= 63 } || !char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || (c == '-' && name[i - 1] != '-')))
            {
                return false;
            }
        }

        return true;
    }
}
