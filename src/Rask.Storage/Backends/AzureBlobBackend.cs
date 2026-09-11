using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

namespace Rask.Storage.Backends;

/// <summary>
/// Blobs in an Azure Storage container, signed in-process with Shared Key — or reached with a SAS token when the
/// connection string carries one instead of a key. No cloud SDK.
/// </summary>
internal sealed class AzureBlobBackend(HttpClient http, AzureAccount account, string container, TimeProvider time)
    : RemoteBlobBackend(http, time)
{
    public override StorageProvider Provider => StorageProvider.Azure;

    public override long MaxSinglePutBytes => AzureStorageOptions.MaxSinglePutBytes;

    public override async Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers,
        CancellationToken cancellationToken)
    {
        RequireKey(key);

        var file = OpenSpool(sourcePath);
        await using (file.ConfigureAwait(false))
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, BlobUri(key, null));
            var content = new StreamContent(file);
            content.Headers.ContentLength = length;
            request.Content = content;

            // A blob's type is fixed when it is created and has no default. The x-ms-blob-content-* headers are
            // what Azure serves the blob with later, so a CDN in front of it gets the same safe headers.
            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
            request.Headers.TryAddWithoutValidation("x-ms-blob-content-type", headers.ContentType);
            request.Headers.TryAddWithoutValidation("x-ms-blob-content-disposition", headers.ContentDisposition);
            request.Headers.TryAddWithoutValidation("x-ms-blob-cache-control", headers.CacheControl);
            Authorize(request);

            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "store a blob", cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken)
    {
        RequireKey(key);

        using var request = new HttpRequestMessage(HttpMethod.Get, BlobUri(key, null));
        if (offset > 0 || count is not null)
        {
            request.Headers.TryAddWithoutValidation("x-ms-range", count is { } c
                ? string.Create(CultureInfo.InvariantCulture, $"bytes={offset}-{offset + c - 1}")
                : string.Create(CultureInfo.InvariantCulture, $"bytes={offset}-"));
        }

        Authorize(request);
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        return await ReadBodyAsync(response, offset, cancellationToken).ConfigureAwait(false);
    }

    public override async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        RequireKey(key);

        using var request = new HttpRequestMessage(HttpMethod.Delete, BlobUri(key, null));
        Authorize(request);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, "delete a blob", cancellationToken).ConfigureAwait(false);
        }
    }

    public override async IAsyncEnumerable<BlobEntry> ListAsync(string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequirePrefix(prefix);
        string? marker = null;

        do
        {
            var query = "restype=container&comp=list&maxresults=5000&prefix=" + Uri.EscapeDataString(prefix);
            if (marker is { Length: > 0 })
            {
                query += "&marker=" + Uri.EscapeDataString(marker);
            }

            List<BlobEntry> page;
            using (var request = new HttpRequestMessage(HttpMethod.Get, BlobUri(null, query)))
            {
                Authorize(request);
                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(response, "list blobs", cancellationToken).ConfigureAwait(false);
                var root = await ReadXmlAsync(response, cancellationToken).ConfigureAwait(false);

                // Azure's listing has no XML namespace, unlike S3's.
                page = (root.Element("Blobs")?.Elements("Blob") ?? [])
                    .Select(static blob =>
                    {
                        var properties = blob.Element("Properties");
                        return new BlobEntry(
                            blob.Element("Name")?.Value ?? "",
                            long.Parse(properties?.Element("Content-Length")?.Value ?? "0", CultureInfo.InvariantCulture),
                            DateTimeOffset.Parse(properties?.Element("Last-Modified")?.Value ?? "Thu, 01 Jan 1970 00:00:00 GMT",
                                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
                    })
                    .ToList();

                marker = root.Element("NextMarker")?.Value;
            }

            foreach (var entry in page)
            {
                yield return entry;
            }
        }
        while (marker is { Length: > 0 });
    }

    public override bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out string? url)
    {
        RequireKey(key);

        // A SAS-only connection string cannot mint a narrower SAS, and handing out the configured one would give
        // every visitor the app's own credential. The app route serves those files instead.
        if (!account.CanSign)
        {
            url = null;
            return false;
        }

        url = $"{account.BlobEndpoint}/{container}/{key}?"
              + AzureSas.BlobRead(account, container, key, Time.GetUtcNow() + lifetime, contentType, contentDisposition);
        return true;
    }

    private Uri BlobUri(string? key, string? query)
    {
        var path = key is null ? $"{account.BlobEndpoint}/{container}" : $"{account.BlobEndpoint}/{container}/{key}";
        var sas = account.CanSign ? null : account.SharedAccessSignature;
        var parts = new[] { query, sas }.Where(static p => p is { Length: > 0 });
        var joined = string.Join('&', parts);
        return new Uri(joined.Length > 0 ? path + "?" + joined : path);
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (account.CanSign)
        {
            AzureSharedKey.Sign(request, account, Time.GetUtcNow());
        }
        else
        {
            request.Headers.TryAddWithoutValidation("x-ms-version", AzureSharedKey.Version);
        }
    }
}
