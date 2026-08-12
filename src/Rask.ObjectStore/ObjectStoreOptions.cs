namespace Rask.ObjectStore;

/// <summary>Where the bucket is and how to reach it.</summary>
public sealed class ObjectStoreOptions
{
    /// <summary>
    ///     The service endpoint — <c>https://s3.us-east-1.amazonaws.com</c>,
    ///     <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>, <c>https://storage.googleapis.com</c>,
    ///     a MinIO address, or <c>https://&lt;account&gt;.blob.core.windows.net</c> for Azure.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>The bucket (S3 and compatible) or container (Azure).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>The signing region. Ignored by Azure. R2 wants <c>auto</c>.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    ///     Whether to address the bucket as a path segment (<c>host/bucket/key</c>) rather than a subdomain
    ///     (<c>bucket.host/key</c>). Path style is the default because R2, MinIO and most S3-compatible
    ///     stores require it; AWS accepts both.
    /// </summary>
    public bool UsePathStyle { get; set; } = true;

    /// <summary>S3 access key id, when credentials come from configuration rather than at runtime.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>S3 secret access key, when credentials come from configuration rather than at runtime.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Optional STS session token that pairs with a temporary access key.</summary>
    public string? SessionToken { get; set; }

    /// <summary>Azure SAS token, with or without its leading <c>?</c>.</summary>
    public string? SasToken { get; set; }

    /// <summary>Throws when the options can't address a bucket.</summary>
    public void Validate()
    {
        if (ServiceUrl is null)
        {
            throw new InvalidOperationException($"{nameof(ObjectStoreOptions)}.{nameof(ServiceUrl)} is required.");
        }

        if (string.IsNullOrWhiteSpace(Bucket))
        {
            throw new InvalidOperationException($"{nameof(ObjectStoreOptions)}.{nameof(Bucket)} is required.");
        }
    }
}

/// <summary>
///     The clock requests are signed against, carrying a correction learned from the service itself.
/// </summary>
/// <remarks>
///     A SigV4 signature is rejected once the request drifts more than 15 minutes from the service's clock,
///     and browser clocks are genuinely wrong often enough that this cannot be assumed away — a user whose
///     machine is a day out would otherwise get a signature error that says nothing about the cause. Every
///     response carries the server's <c>Date</c>, so the first one teaches the offset and later requests
///     sign against corrected time. This makes a wrong local clock a non-event instead of a support ticket.
/// </remarks>
internal sealed class ObjectStoreClock
{
    private long _offsetTicks;

    /// <summary>Now, corrected by whatever offset has been observed.</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Whether a correction has been learned.</summary>
    public bool IsCorrected => Interlocked.Read(ref _offsetTicks) != 0;

    /// <summary>Records the service's own time, so subsequent signatures use it.</summary>
    public void Observe(DateTimeOffset serverTime) =>
        Interlocked.Exchange(ref _offsetTicks, (serverTime - DateTimeOffset.UtcNow).Ticks);
}
