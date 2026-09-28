namespace Rask.Storage;

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
    /// <c>https://storage.googleapis.com</c>, or a MinIO address. Configuration: <c>Rask__Storage__S3__ServiceUrl</c>.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>The bucket. Configuration: <c>Rask__Storage__S3__Bucket</c>.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>The signing region. Default <c>us-east-1</c>; Cloudflare R2 wants <c>auto</c>. Configuration: <c>Rask__Storage__S3__Region</c>.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>The access key id. Configuration: <c>Rask__Storage__S3__AccessKeyId</c>.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>The secret access key. Configuration: <c>Rask__Storage__S3__SecretAccessKey</c> — pass it as a secret.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>An STS session token that pairs with a temporary access key. Configuration: <c>Rask__Storage__S3__SessionToken</c>.</summary>
    public string? SessionToken { get; set; }

    /// <summary>
    /// Whether the bucket is a path segment (<c>host/bucket/key</c>) rather than a subdomain
    /// (<c>bucket.host/key</c>). Default <c>true</c>, which R2, MinIO and most compatible stores require.
    /// Configuration: <c>Rask__Storage__S3__UsePathStyle</c>.
    /// </summary>
    public bool UsePathStyle { get; set; } = true;

    internal void Validate()
    {
        if (ServiceUrl is null || !ServiceUrl.IsAbsoluteUri || ServiceUrl.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "Rask__Storage__S3__ServiceUrl is required when Rask__Storage__Provider is S3: an absolute URL like "
                + "https://s3.us-east-1.amazonaws.com, https://<account>.r2.cloudflarestorage.com or http://localhost:9000.");
        }

        if (!IsBucketName(Bucket))
        {
            throw new InvalidOperationException(
                $"Rask__Storage__S3__Bucket '{Bucket}' is not a bucket name: 3 to 63 lowercase letters, digits, '.' and '-', "
                + "starting and ending with a letter or digit.");
        }

        if (string.IsNullOrWhiteSpace(Region))
        {
            throw new InvalidOperationException("Rask__Storage__S3__Region is required, like us-east-1 (Cloudflare R2: auto).");
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            throw new InvalidOperationException(
                "Rask__Storage__S3__AccessKeyId and Rask__Storage__S3__SecretAccessKey are both required when Rask__Storage__Provider is S3. "
                + "Pass the secret with rask deploy --env, never in appsettings.json.");
        }

        // A bucket with dots under a wildcard certificate: bucket.name.s3.example.com matches no *.s3.example.com.
        if (!UsePathStyle && string.Equals(ServiceUrl.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && Bucket.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The bucket '{Bucket}' contains dots, which breaks TLS in virtual-host addressing. Set Rask__Storage__S3__UsePathStyle to true.");
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
