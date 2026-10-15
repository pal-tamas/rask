using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Rask.Server.Http;

/// <summary>
///     The client runtime as it is served: encoded once, named by its content hash, compressed once per encoding.
/// </summary>
/// <remarks>
///     The page asks for it as <c>rask.js?v={hash}</c>, which a browser may keep forever because the hash moves
///     with the bytes. Any other URL is revalidated, so an old page cannot pin an old runtime under the bare name.
///     The serving itself is <see cref="HashedAsset" />'s, shared with the app's stylesheet.
/// </remarks>
internal sealed class RuntimeScript
{
    private const string ContentType = "text/javascript; charset=utf-8";
    private const int HashLength = 16;

    private readonly HashedAsset _asset;

    public RuntimeScript(string source)
        : this(source, HashOf(source))
    {
    }

    /// <summary>A script named by a hash that is not its own alone: one of several that are released together.</summary>
    public RuntimeScript(string source, string hash) =>
        _asset = new HashedAsset(Encoding.UTF8.GetBytes(source), ContentType, hash);

    public string Hash => _asset.Hash;

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

    public Task Serve(HttpContext ctx) => _asset.Serve(ctx);
}
