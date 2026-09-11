using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
using Rask.Core.ScopedAssets;

namespace Rask.Server.Prerender;

/// <summary>
///     One stored copy of a public page: its bytes, their compressed forms, and what they were made from.
/// </summary>
/// <remarks>
///     Immutable and replaced whole, so a request reads a complete copy without taking a lock while a
///     background render writes the next one.
/// </remarks>
internal sealed class PageCacheEntry
{
    private PageCacheEntry(
        byte[] identity,
        byte[]? brotli,
        byte[]? gzip,
        string hash,
        bool readsUser,
        long renderedAt,
        DateTimeOffset contentChangedAt,
        string cssBundle,
        string jsBundle)
    {
        Identity = identity;
        Brotli = brotli;
        Gzip = gzip;
        Hash = hash;
        ReadsUser = readsUser;
        RenderedAt = renderedAt;
        ContentChangedAt = contentChangedAt;
        CssBundle = cssBundle;
        JsBundle = jsBundle;

        // Built once per copy rather than once per response: every request for the page sends one of these.
        IdentityTag = new EntityTagHeaderValue("\"" + hash + "\"");
        BrotliTag = brotli is null ? null : new EntityTagHeaderValue("\"" + hash + "-br\"");
        GzipTag = gzip is null ? null : new EntityTagHeaderValue("\"" + hash + "-gzip\"");
    }

    /// <summary>The document, as UTF-8.</summary>
    internal byte[] Identity { get; }

    /// <summary>The document compressed with Brotli, or <c>null</c> when that was no smaller.</summary>
    internal byte[]? Brotli { get; }

    /// <summary>The document compressed with gzip, or <c>null</c> when that was no smaller.</summary>
    internal byte[]? Gzip { get; }

    /// <summary>A prefix of the document's SHA-256, in hex: what its ETags are made from.</summary>
    internal string Hash { get; }

    /// <summary>The strong ETag of <see cref="Identity" />.</summary>
    internal EntityTagHeaderValue IdentityTag { get; }

    /// <summary>The ETag of <see cref="Brotli" />, suffixed the way the scoped assets' are.</summary>
    internal EntityTagHeaderValue? BrotliTag { get; }

    /// <summary>The ETag of <see cref="Gzip" />.</summary>
    internal EntityTagHeaderValue? GzipTag { get; }

    /// <summary>
    ///     Whether the render read the signed-in user — if so, the copy is served only to visitors who are
    ///     not signed in.
    /// </summary>
    internal bool ReadsUser { get; }

    /// <summary>When the render that produced this copy finished, as a <see cref="TimeProvider" /> timestamp.</summary>
    internal long RenderedAt { get; }

    /// <summary>When the document's bytes last changed — the <c>Last-Modified</c> it is served with.</summary>
    internal DateTimeOffset ContentChangedAt { get; }

    /// <summary>The scoped CSS bundle the document links to.</summary>
    internal string CssBundle { get; }

    /// <summary>The scoped JS bundle the document links to.</summary>
    internal string JsBundle { get; }

    /// <summary>What the copy costs in memory, compressed forms included.</summary>
    internal long Bytes => Identity.LongLength + (Brotli?.LongLength ?? 0) + (Gzip?.LongLength ?? 0);

    /// <summary>
    ///     A copy of <paramref name="document" />.
    /// </summary>
    /// <param name="document">The document to store.</param>
    /// <param name="readsUser">Whether its render read the signed-in user.</param>
    /// <param name="renderedAt">When the render finished, as a <see cref="TimeProvider" /> timestamp.</param>
    /// <param name="now">The wall-clock time, for <see cref="ContentChangedAt" />.</param>
    /// <param name="cssBundle">The scoped CSS bundle hash the document was rendered against.</param>
    /// <param name="jsBundle">The scoped JS bundle hash the document was rendered against.</param>
    /// <param name="previous">The copy this replaces, if any.</param>
    /// <remarks>
    ///     A render that produced the same bytes as <paramref name="previous" /> — the ordinary case for a page
    ///     refreshed on a timer — reuses its arrays and keeps its <see cref="ContentChangedAt" />. That skips
    ///     compressing the page again, keeps a large page's arrays from being churned on every refresh, and
    ///     keeps the ETag a browser holds valid, so its next visit is a 304.
    /// </remarks>
    internal static PageCacheEntry Create(
        string document,
        bool readsUser,
        long renderedAt,
        DateTimeOffset now,
        string cssBundle,
        string jsBundle,
        PageCacheEntry? previous)
    {
        ArgumentNullException.ThrowIfNull(document);

        var identity = Encoding.UTF8.GetBytes(document);
        var hash = Convert.ToHexStringLower(SHA256.HashData(identity).AsSpan(0, 16));

        if (previous is not null && string.Equals(previous.Hash, hash, StringComparison.Ordinal))
        {
            return new PageCacheEntry(
                previous.Identity,
                previous.Brotli,
                previous.Gzip,
                hash,
                readsUser,
                renderedAt,
                previous.ContentChangedAt,
                cssBundle,
                jsBundle);
        }

        return new PageCacheEntry(
            identity,
            SmallerThan(identity, ScopedAssetCompression.Compress(identity, "br")),
            SmallerThan(identity, ScopedAssetCompression.Compress(identity, "gzip")),
            hash,
            readsUser,
            renderedAt,
            now,
            cssBundle,
            jsBundle);
    }

    // A compressed form that is not smaller costs memory and a header to say nothing.
    private static byte[]? SmallerThan(byte[] identity, byte[] compressed) =>
        compressed.Length < identity.Length ? compressed : null;
}
