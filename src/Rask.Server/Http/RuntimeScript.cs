using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Rask.Core.ScopedAssets;
using Rask.Hosting.Shared;

namespace Rask.Server.Http;

/// <summary>
///     The client runtime as it is served: encoded once, named by its content hash, compressed once per encoding.
/// </summary>
/// <remarks>
///     The page asks for it as <c>rask.js?v={hash}</c>, which a browser may keep forever because the hash moves
///     with the bytes. Any other URL is revalidated, so an old page cannot pin an old runtime under the bare name.
/// </remarks>
internal sealed class RuntimeScript
{
    private const string ContentType = "text/javascript; charset=utf-8";
    private const int HashLength = 16;

    private readonly ConcurrentDictionary<string, byte[]> _encoded = new(StringComparer.Ordinal);
    private readonly byte[] _utf8;

    public RuntimeScript(string source)
        : this(source, HashOf(source))
    {
    }

    /// <summary>A script named by a hash that is not its own alone: one of several that are released together.</summary>
    public RuntimeScript(string source, string hash)
    {
        _utf8 = Encoding.UTF8.GetBytes(source);
        Hash = hash;
    }

    public string Hash { get; }

    /// <summary>The name of <paramref name="sources" /> taken together: it moves when any of them does.</summary>
    public static string HashOf(params ReadOnlySpan<string> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var source in sources)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(source));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..HashLength];
    }

    public Task Serve(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        headers.CacheControl = IsNamedByHash(ctx.Request) ? "public, max-age=31536000, immutable" : "no-cache";
        headers["X-Content-Type-Options"] = "nosniff";
        headers.Vary = "Accept-Encoding";

        var encoding = ContentEncodingNegotiation.Negotiate(ctx.Request);
        if (encoding is null)
        {
            return Results.Bytes(_utf8, ContentType, entityTag: Etag(Hash)).ExecuteAsync(ctx);
        }

        headers.ContentEncoding = encoding;
        var bytes = _encoded.GetOrAdd(encoding, static (name, utf8) => ScopedAssetCompression.Compress(utf8, name), _utf8);
        return Results.Bytes(bytes, ContentType, entityTag: Etag(Hash + "-" + encoding)).ExecuteAsync(ctx);
    }

    private bool IsNamedByHash(HttpRequest request) =>
        string.Equals(request.Query["v"], Hash, StringComparison.Ordinal);

    private static EntityTagHeaderValue Etag(string value) => new("\"" + value + "\"");
}
