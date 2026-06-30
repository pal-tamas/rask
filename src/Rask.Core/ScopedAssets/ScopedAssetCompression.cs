using System.Collections.Concurrent;
using System.IO.Compression;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     Brotli/gzip representations of the content-addressed scoped assets served at
///     <c>/_rask/a/{hash}.{ext}</c> (the per-kind bundles). Because every asset is keyed by the SHA-256
///     of its bytes and served <c>immutable</c>, each compressed representation is built exactly once
///     and cached for the app's lifetime — a request can never observe a stale entry. Both the Server
///     (<c>RaskEndpointExtensions.ServeAssetAsync</c>) and the published-WASM host
///     (<c>RaskAssetEndpoint</c>) share this so the on-the-wire bytes match across hosts.
/// </summary>
public static class ScopedAssetCompression
{
    // Keyed "{hash}.{ext}.{encoding}". Bounded by (distinct asset hashes × 2 encodings); a hot-reload
    // hash change simply adds a new key and leaves the now-unreferenced old one (bounded by reloads).
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new();

    /// <summary>
    ///     Picks the best content-encoding the client advertises — brotli over gzip — or <c>null</c> for
    ///     identity (no/blank/unknown <c>Accept-Encoding</c>). Tokens are matched whole (so <c>br</c>
    ///     never matches inside another token) with their <c>;q=…</c> parameters stripped; an explicit
    ///     <c>q=0</c> opt-out is not parsed (vanishingly rare for the static <c>gzip, deflate, br</c>
    ///     header browsers send). Deflate is intentionally not offered — br/gzip cover every modern
    ///     client and gzip is the universal floor.
    /// </summary>
    public static string? Negotiate(string? acceptEncoding)
    {
        if (string.IsNullOrEmpty(acceptEncoding))
        {
            return null;
        }

        var hasGzip = false;
        foreach (var part in acceptEncoding.Split(','))
        {
            var token = part.Trim();
            var semi = token.IndexOf(';');
            if (semi >= 0)
            {
                token = token[..semi].Trim();
            }

            if (token.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                return "br"; // best available — return immediately.
            }

            if (token.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                hasGzip = true;
            }
        }

        return hasGzip ? "gzip" : null;
    }

    /// <summary>
    ///     Returns the asset's bytes encoded with <paramref name="encoding" /> (<c>"br"</c> or
    ///     <c>"gzip"</c>) plus an encoding-suffixed strong ETag, building + caching the compressed bytes
    ///     on first use. Returns <c>null</c> when the hash is unknown for the kind, so the caller can
    ///     fall back to a 404 exactly as the identity path does.
    /// </summary>
    public static (byte[] Bytes, string Etag)? GetEncoded(string hash, AssetKind kind, string encoding)
    {
        var ext = kind == AssetKind.Css ? "css" : "js";
        var key = hash + "." + ext + "." + encoding;
        var etag = "\"" + hash + "-" + encoding + "\"";

        if (_cache.TryGetValue(key, out var cached))
        {
            return (cached, etag);
        }

        var asset = ScopedAssetRegistry.GetByHash(hash, kind);
        if (asset is null)
        {
            return null;
        }

        var compressed = Compress(asset.Value.Utf8.Span, encoding);
        _cache[key] = compressed;
        return (compressed, etag);
    }

    private static byte[] Compress(ReadOnlySpan<byte> data, string encoding)
    {
        using var ms = new MemoryStream();
        if (encoding == "br")
        {
            using (var br = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                br.Write(data);
            }
        }
        else
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                gz.Write(data);
            }
        }

        return ms.ToArray();
    }
}
