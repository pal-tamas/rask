using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Rask.ObjectStore;

/// <summary>
///     <see cref="IObjectStore" /> over Azure Blob Storage. Authentication is a SAS token appended to the
///     URL, so unlike the S3 path there is nothing to sign — which also means no clock sensitivity, and a
///     credential that can be handed to a browser already scoped and already expiring.
/// </summary>
public sealed class AzureBlobObjectStore : IObjectStore
{
    private readonly IObjectStoreCredentials _credentials;
    private readonly HttpClient _http;
    private readonly ObjectStoreOptions _options;

    /// <summary>Creates a store over <paramref name="options" />.</summary>
    public AzureBlobObjectStore(
        HttpClient http, IObjectStoreCredentials credentials, IOptions<ObjectStoreOptions> options)
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

        using var request = new HttpRequestMessage(HttpMethod.Get, await KeyUriAsync(key, cancellationToken).ConfigureAwait(false));
        request.Headers.TryAddWithoutValidation("Range", $"bytes={offset}-{offset + count - 1}");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // The blob exists but the range starts past its end — an empty read, not a missing object.
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

        // The returned stream owns the response, so it is deliberately not disposed on the success path.
        var response = await _http
            .GetAsync(await KeyUriAsync(key, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
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
    public Task PutAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return PutContentAsync(key, new ByteArrayContent(content), null, cancellationToken);
    }

    /// <inheritdoc />
    public Task PutAsync(string key, Stream content, long length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentLength = length;
        return PutContentAsync(key, streamContent, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var status = await PutContentAsync(key, new ByteArrayContent(content), "*", cancellationToken)
            .ConfigureAwait(false);

        return status != HttpStatusCode.PreconditionFailed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObjectEntry>> ListAsync(
        string prefix, string? startAfter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var entries = new List<ObjectEntry>();
        string? marker = null;

        do
        {
            using var response = await _http
                .GetAsync(await ListUriAsync(prefix, marker, cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = XDocument.Parse(body).Root!;

            // Azure's blob list is in no namespace, unlike S3's.
            foreach (var blob in root.Element("Blobs")?.Elements("Blob") ?? [])
            {
                var name = blob.Element("Name")!.Value;

                // Azure has no start-after, so the skip happens here. The listing still costs the same —
                // only the caller is spared re-reading objects it already has.
                if (startAfter is { Length: > 0 } && string.CompareOrdinal(name, startAfter) <= 0)
                {
                    continue;
                }

                var properties = blob.Element("Properties");
                entries.Add(new ObjectEntry(
                    name,
                    long.Parse(properties?.Element("Content-Length")?.Value ?? "0", CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(
                        properties?.Element("Last-Modified")?.Value ?? "1970-01-01T00:00:00Z",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    properties?.Element("Etag")?.Value.Trim('"')));
            }

            marker = root.Element("NextMarker")?.Value;
        }
        while (marker is { Length: > 0 });

        return entries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListPrefixesAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var prefixes = new List<string>();
        string? marker = null;

        do
        {
            using var response = await _http
                .GetAsync(
                    await ListUriAsync(prefix, marker, cancellationToken, delimiter: true).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = XDocument.Parse(body).Root!;

            foreach (var blobPrefix in root.Element("Blobs")?.Elements("BlobPrefix") ?? [])
            {
                if (blobPrefix.Element("Name")?.Value is { Length: > 0 } value)
                {
                    prefixes.Add(value);
                }
            }

            marker = root.Element("NextMarker")?.Value;
        }
        while (marker is { Length: > 0 });

        return prefixes;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var response = await _http
            .DeleteAsync(await KeyUriAsync(key, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpStatusCode> PutContentAsync(
        string key, HttpContent content, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, await KeyUriAsync(key, cancellationToken).ConfigureAwait(false))
        {
            Content = content,
        };

        // Without this header Azure rejects the write: a blob's type is fixed at creation and has no default.
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (ifNoneMatch is not null && response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return response.StatusCode;
        }

        response.EnsureSuccessStatusCode();
        return response.StatusCode;
    }

    private async Task<Uri> KeyUriAsync(string key, CancellationToken cancellationToken)
    {
        var sas = await RequireSasAsync(cancellationToken).ConfigureAwait(false);
        var path = string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"{ContainerBase()}/{path}?{sas}");
    }

    private async Task<Uri> ListUriAsync(
        string prefix, string? marker, CancellationToken cancellationToken, bool delimiter = false)
    {
        var sas = await RequireSasAsync(cancellationToken).ConfigureAwait(false);
        var query = $"restype=container&comp=list&prefix={Uri.EscapeDataString(prefix)}";
        if (delimiter)
        {
            query += "&delimiter=%2F";
        }

        if (marker is { Length: > 0 })
        {
            query += $"&marker={Uri.EscapeDataString(marker)}";
        }

        return new Uri($"{ContainerBase()}?{query}&{sas}");
    }

    private string ContainerBase() =>
        $"{_options.ServiceUrl!.GetLeftPart(UriPartial.Authority)}/{_options.Bucket}";

    private async Task<string> RequireSasAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentials.GetAsync(cancellationToken).ConfigureAwait(false);
        if (credential?.SasToken is not { Length: > 0 } sas)
        {
            throw new InvalidOperationException(
                "No Azure SAS token is available. Register one through IObjectStoreCredentials — " +
                "InMemoryObjectStoreCredentials.Set(...) for a token supplied at runtime, or " +
                "ObjectStoreOptions.SasToken for one from configuration.");
        }

        return sas.TrimStart('?');
    }
}
