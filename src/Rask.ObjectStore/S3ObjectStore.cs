using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Rask.ObjectStore;

/// <summary>
///     <see cref="IObjectStore" /> over Amazon S3 and everything that speaks its REST API — Cloudflare R2,
///     Google Cloud Storage through its S3 interop keys, MinIO, Backblaze B2, DigitalOcean Spaces. Requests
///     are signed in-process with <see cref="SigV4Signer" />, so no cloud SDK is involved and the same code
///     runs in a browser.
/// </summary>
public sealed class S3ObjectStore : IObjectStore
{
    private const string Service = "s3";
    private static readonly XNamespace S3Ns = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly ObjectStoreClock _clock = new();
    private readonly IObjectStoreCredentials _credentials;
    private readonly HttpClient _http;
    private readonly ObjectStoreOptions _options;

    /// <summary>Creates a store over <paramref name="options" />.</summary>
    public S3ObjectStore(HttpClient http, IObjectStoreCredentials credentials, IOptions<ObjectStoreOptions> options)
    {
        _http = http;
        _credentials = credentials;
        _options = options.Value;
        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetRangeAsync(
        string key, long offset, int count, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            return [];
        }

        using var response = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, KeyUri(key));
                // Inclusive on both ends, so the last byte is offset + count - 1.
                request.Headers.TryAddWithoutValidation("Range", $"bytes={offset}-{offset + count - 1}");
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        if (IsMissing(response))
        {
            return null;
        }

        // A range starting past the end is 416 rather than an empty 206 — the object exists, so answer with
        // an empty read instead of null, which would say it doesn't.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Not disposed on the success path: the returned stream owns the response, and disposing the
        // message here would close the stream out from under the caller.
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, KeyUri(key)), cancellationToken).ConfigureAwait(false);

        if (IsMissing(response))
        {
            response.Dispose();
            return null;
        }

        try
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Put, KeyUri(key)) { Content = new ByteArrayContent(content) },
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string key, Stream content, long length, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // A retry would have to rewind the stream, and a forward-only stream can't be rewound — so this
        // path deliberately signs once. The clock correction below is what makes that safe in practice.
        await EnsureClockAsync(cancellationToken).ConfigureAwait(false);

        var credential = await RequireCredentialAsync(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Put, KeyUri(key));
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentLength = length;
        request.Content = streamContent;
        SigV4Signer.Sign(request, credential, _options.Region, Service, _clock.UtcNow);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        ObserveClock(response);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        using var response = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, KeyUri(key))
                {
                    Content = new ByteArrayContent(content),
                };
                request.Headers.TryAddWithoutValidation("If-None-Match", "*");
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        // The loser of the race gets 412. Some S3-compatible stores answer 409 instead; both mean the
        // object was already there, which is the answer the caller asked for.
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObjectEntry>> ListAsync(
        string prefix, string? startAfter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var entries = new List<ObjectEntry>();
        string? continuationToken = null;

        do
        {
            var token = continuationToken;
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, ListUri(prefix, token, startAfter)),
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = XDocument.Parse(body).Root!;

            foreach (var contents in root.Elements(S3Ns + "Contents"))
            {
                entries.Add(new ObjectEntry(
                    contents.Element(S3Ns + "Key")!.Value,
                    long.Parse(contents.Element(S3Ns + "Size")!.Value, CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(
                        contents.Element(S3Ns + "LastModified")!.Value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    contents.Element(S3Ns + "ETag")?.Value.Trim('"')));
            }

            continuationToken =
                string.Equals(root.Element(S3Ns + "IsTruncated")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                    ? root.Element(S3Ns + "NextContinuationToken")?.Value
                    : null;
        }
        while (continuationToken is { Length: > 0 });

        return entries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListPrefixesAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var prefixes = new List<string>();
        string? continuationToken = null;

        do
        {
            var token = continuationToken;
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, ListUri(prefix, token, delimiter: true)),
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = XDocument.Parse(body).Root!;

            foreach (var common in root.Elements(S3Ns + "CommonPrefixes"))
            {
                if (common.Element(S3Ns + "Prefix")?.Value is { Length: > 0 } value)
                {
                    prefixes.Add(value);
                }
            }

            continuationToken =
                string.Equals(root.Element(S3Ns + "IsTruncated")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                    ? root.Element(S3Ns + "NextContinuationToken")?.Value
                    : null;
        }
        while (continuationToken is { Length: > 0 });

        return prefixes;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, KeyUri(key)), cancellationToken).ConfigureAwait(false);

        // S3 answers 204 whether or not the key was there, so a missing object is already a non-event.
        if (IsMissing(response))
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     Signs and sends, retrying once against the service's own clock if the signature was rejected as
    ///     skewed. The request is rebuilt rather than reused because a sent <see cref="HttpRequestMessage" />
    ///     cannot be sent twice, and its signature headers would otherwise be signed over twice.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        var credential = await RequireCredentialAsync(cancellationToken).ConfigureAwait(false);

        var request = build();
        SigV4Signer.Sign(request, credential, _options.Region, Service, _clock.UtcNow);
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        request.Dispose();

        if (!IsSkewRejection(response) || _clock.IsCorrected)
        {
            ObserveClock(response);
            return response;
        }

        // The local clock is far enough out that the service refuses to talk to us. Its Date header is the
        // authority; learn from it and sign again, so a wrong device clock costs one round trip instead of
        // failing with an error that says nothing about the cause.
        if (response.Headers.Date is not { } serverTime)
        {
            return response;
        }

        _clock.Observe(serverTime);
        response.Dispose();

        var retry = build();
        SigV4Signer.Sign(retry, credential, _options.Region, Service, _clock.UtcNow);
        var retried = await _http.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        retry.Dispose();
        return retried;
    }

    private async Task EnsureClockAsync(CancellationToken cancellationToken)
    {
        if (_clock.IsCorrected)
        {
            return;
        }

        // A HEAD on the bucket is cheap and its Date teaches the offset — worth one round trip before a
        // stream upload that cannot be retried.
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Head, ListUri(string.Empty, null));
            var credential = await RequireCredentialAsync(cancellationToken).ConfigureAwait(false);
            SigV4Signer.Sign(probe, credential, _options.Region, Service, _clock.UtcNow);
            using var response = await _http.SendAsync(probe, cancellationToken).ConfigureAwait(false);
            ObserveClock(response);
        }
        catch (HttpRequestException)
        {
            // Best effort: if the probe can't reach the service, the upload will report the real problem.
        }
    }

    private void ObserveClock(HttpResponseMessage response)
    {
        if (!_clock.IsCorrected && response.Headers.Date is { } serverTime &&
            (serverTime - DateTimeOffset.UtcNow).Duration() > TimeSpan.FromMinutes(5))
        {
            _clock.Observe(serverTime);
        }
    }

    private async Task<ObjectStoreCredential> RequireCredentialAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentials.GetAsync(cancellationToken).ConfigureAwait(false);
        if (credential?.AccessKeyId is not { Length: > 0 } || credential.SecretAccessKey is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "No S3 credential is available. Register one through IObjectStoreCredentials — " +
                "InMemoryObjectStoreCredentials.Set(...) for a credential supplied at runtime, or " +
                "ObjectStoreOptions.AccessKeyId/SecretAccessKey for one from configuration.");
        }

        return credential;
    }

    private static bool IsMissing(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.NotFound;

    // 403 covers both a skewed signature and a genuinely wrong key; retrying once when the clock has not
    // been corrected yet is cheap, and a real auth failure simply fails the same way the second time.
    private static bool IsSkewRejection(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest;

    // Concatenated rather than built with the (Uri, string) constructor: relative-URI resolution would
    // reinterpret a key that happens to start with "/" as rooted, silently dropping the bucket segment.
    private Uri KeyUri(string key) => new(BucketBase() + EncodeKey(key));

    private Uri ListUri(string prefix, string? continuationToken, string? startAfter = null, bool delimiter = false)
    {
        var query = $"?list-type=2&prefix={Uri.EscapeDataString(prefix)}";
        if (delimiter)
        {
            query += "&delimiter=%2F";
        }

        if (continuationToken is { Length: > 0 })
        {
            query += $"&continuation-token={Uri.EscapeDataString(continuationToken)}";
        }
        // Only meaningful on the first page: once a continuation token is in play the service is already
        // resuming from where it stopped, and sending both is contradictory.
        else if (startAfter is { Length: > 0 })
        {
            query += $"&start-after={Uri.EscapeDataString(startAfter)}";
        }

        return new Uri(BucketBase() + query);
    }

    private string BucketBase()
    {
        var service = _options.ServiceUrl!;
        var root = service.GetLeftPart(UriPartial.Authority);
        return _options.UsePathStyle
            ? $"{root}/{_options.Bucket}/"
            : $"{service.Scheme}://{_options.Bucket}.{service.Authority}/";
    }

    // Slashes stay slashes so a key reads as a path in the bucket; everything else is escaped.
    private static string EncodeKey(string key) =>
        string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
}
