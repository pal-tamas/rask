using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Rask.Core.ScopedAssets;
using Rask.Hosting.Shared;

namespace Rask.Server.Http;

/// <summary>
///     A file as Rask serves it: named by its content hash, compressed once per encoding, revalidated by its ETag.
/// </summary>
/// <remarks>
///     A request that names the hash (<c>?v={hash}</c>) may be kept by the browser for a year, because the hash moves
///     with the bytes; any other request is <c>no-cache</c>, so the browser asks again and gets a 304 while nothing
///     changed. Shared by the client runtime (<see cref="RuntimeScript" />) and the app's stylesheet
///     (<see cref="AppStylesheet" />).
/// </remarks>
internal sealed class HashedAsset(byte[] bytes, string contentType, string hash)
{
    private const int HashLength = 16;

    private readonly ConcurrentDictionary<string, byte[]> _encoded = new(StringComparer.Ordinal);

    public string Hash { get; } = hash;

    /// <summary>The first 16 hex digits of the bytes' SHA-256: what names an asset in its URL.</summary>
    public static string HashOf(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..HashLength];

    /// <summary>Writes the asset, encoded as the request accepts, with the cache headers its URL earns.</summary>
    public Task Serve(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        headers.CacheControl = IsNamedByHash(ctx.Request) ? "public, max-age=31536000, immutable" : "no-cache";
        headers["X-Content-Type-Options"] = "nosniff";
        headers.Vary = "Accept-Encoding";

        var encoding = ContentEncodingNegotiation.Negotiate(ctx.Request);
        if (encoding is null)
        {
            return Results.Bytes(bytes, contentType, entityTag: Etag(Hash)).ExecuteAsync(ctx);
        }

        headers.ContentEncoding = encoding;
        var encoded = _encoded.GetOrAdd(encoding, static (name, raw) => ScopedAssetCompression.Compress(raw, name), bytes);
        return Results.Bytes(encoded, contentType, entityTag: Etag(Hash + "-" + encoding)).ExecuteAsync(ctx);
    }

    private bool IsNamedByHash(HttpRequest request) =>
        string.Equals(request.Query["v"], Hash, StringComparison.Ordinal);

    private static EntityTagHeaderValue Etag(string value) => new("\"" + value + "\"");
}
