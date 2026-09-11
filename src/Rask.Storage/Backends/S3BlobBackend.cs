using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Rask.Storage.Backends;

/// <summary>
/// Objects in an S3-compatible bucket, signed in-process with <see cref="SigV4"/> — no cloud SDK.
/// </summary>
internal sealed class S3BlobBackend : RemoteBlobBackend
{
    private static readonly XNamespace S3Ns = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly S3Credential _credential;
    private readonly string _region;
    private readonly string _bucket;
    private readonly bool _pathStyle;
    private readonly string _scheme;
    private readonly string _host;

    internal S3BlobBackend(HttpClient http, S3StorageOptions options, TimeProvider time)
        : base(http, time)
    {
        var service = options.ServiceUrl ?? throw new InvalidOperationException("Storage__S3__ServiceUrl is required.");
        _credential = new S3Credential(options.AccessKeyId!, options.SecretAccessKey!, options.SessionToken);
        _region = options.Region;
        _bucket = options.Bucket;
        _pathStyle = options.UsePathStyle;
        _scheme = service.Scheme;
        _host = _pathStyle ? SigV4.HostOf(service) : _bucket + "." + SigV4.HostOf(service);
    }

    public override StorageProvider Provider => StorageProvider.S3;

    public override long MaxSinglePutBytes => S3StorageOptions.MaxSinglePutBytes;

    public override async Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers,
        CancellationToken cancellationToken)
    {
        RequireKey(key);

        var file = OpenSpool(sourcePath);
        await using (file.ConfigureAwait(false))
        {
            var (request, path, query) = Request(HttpMethod.Put, key, []);
            using (request)
            {
                var content = new StreamContent(file);
                content.Headers.ContentLength = length;
                content.Headers.TryAddWithoutValidation("Content-Type", headers.ContentType);
                content.Headers.TryAddWithoutValidation("Content-Disposition", headers.ContentDisposition);
                request.Content = content;
                request.Headers.TryAddWithoutValidation("Cache-Control", headers.CacheControl);
                Sign(request, path, query);

                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(response, "store an object", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override async Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken)
    {
        RequireKey(key);

        var (request, path, query) = Request(HttpMethod.Get, key, []);
        using (request)
        {
            if (offset > 0 || count is not null)
            {
                request.Headers.TryAddWithoutValidation("Range", Range(offset, count));
            }

            Sign(request, path, query);
            var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await ReadBodyAsync(response, offset, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        RequireKey(key);

        var (request, path, query) = Request(HttpMethod.Delete, key, []);
        using (request)
        {
            Sign(request, path, query);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(response, "delete an object", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override async IAsyncEnumerable<BlobEntry> ListAsync(string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequirePrefix(prefix);
        string? continuation = null;

        do
        {
            var parameters = new List<KeyValuePair<string, string>> { new("list-type", "2"), new("prefix", prefix) };
            if (continuation is { Length: > 0 })
            {
                parameters.Add(new("continuation-token", continuation));
            }

            List<BlobEntry> page;
            var (request, path, query) = Request(HttpMethod.Get, null, parameters);
            using (request)
            {
                Sign(request, path, query);
                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(response, "list objects", cancellationToken).ConfigureAwait(false);
                var root = await ReadXmlAsync(response, cancellationToken).ConfigureAwait(false);

                page = root.Elements(S3Ns + "Contents")
                    .Select(static c => new BlobEntry(
                        c.Element(S3Ns + "Key")?.Value ?? "",
                        long.Parse(c.Element(S3Ns + "Size")?.Value ?? "0", CultureInfo.InvariantCulture),
                        DateTimeOffset.Parse(c.Element(S3Ns + "LastModified")?.Value ?? "1970-01-01T00:00:00Z",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)))
                    .ToList();

                continuation = string.Equals(root.Element(S3Ns + "IsTruncated")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                    ? root.Element(S3Ns + "NextContinuationToken")?.Value
                    : null;
            }

            foreach (var entry in page)
            {
                yield return entry;
            }
        }
        while (continuation is { Length: > 0 });
    }

    public override bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out string? url)
    {
        RequireKey(key);
        url = SigV4.Presign(_scheme, _host, Segments(key),
            [new("response-content-disposition", contentDisposition), new("response-content-type", contentType)],
            _credential, _region, Time.GetUtcNow(), lifetime);
        return true;
    }

    private (HttpRequestMessage Request, string Path, string Query) Request(HttpMethod method, string? key,
        IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        var path = SigV4.CanonicalPath(Segments(key));
        var query = SigV4.CanonicalQuery(parameters);
        var uri = new Uri($"{_scheme}://{_host}{path}{(query.Length > 0 ? "?" + query : "")}");
        return (new HttpRequestMessage(method, uri), path, query);
    }

    private IEnumerable<string> Segments(string? key)
    {
        if (_pathStyle)
        {
            yield return _bucket;
        }

        if (key is null)
        {
            yield return "";
            yield break;
        }

        foreach (var segment in key.Split('/'))
        {
            yield return segment;
        }
    }

    private void Sign(HttpRequestMessage request, string path, string query) =>
        SigV4.SignHeaders(request, path, query, _credential, _region, Time.GetUtcNow());

    private static string Range(long offset, long? count) =>
        count is { } c
            ? string.Create(CultureInfo.InvariantCulture, $"bytes={offset}-{offset + c - 1}")
            : string.Create(CultureInfo.InvariantCulture, $"bytes={offset}-");
}
