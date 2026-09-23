using System.Buffers;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Rask.Server.Http;

/// <summary>
///     Writes the page document, compressed when the client accepts it. See
///     <see cref="RaskServerOptions.CompressPageHtml" />.
/// </summary>
/// <remarks>
///     <para>
///         Done HERE, in the one handler that writes <c>text/html</c>, rather than by putting
///         <c>UseResponseCompression()</c> in the pipeline. That middleware is app-wide: inserted by
///         <c>MapRask</c> it wraps every endpoint after it, so an app's own JSON API would be compressed
///         over HTTPS too — the BREACH shape (a secret beside reflected input) on responses this option
///         never analysed and does not name. Registering <c>AddResponseCompression</c> to get
///         ASP.NET's provider instead would mutate the app-global <c>ResponseCompressionOptions</c>, turning
///         <c>EnableForHttps</c> on for an app's OWN middleware. Scoped to the page, neither can happen.
///     </para>
///     <para>
///         An app that already compresses keeps working: its middleware sees <c>Content-Encoding</c> set
///         and leaves the body alone, and a response somebody else already encoded is written as-is.
///     </para>
///     <para>
///         <c>CompressionLevel.Optimal</c>, measured on rask.sh's 78,525-byte landing page: 0.28&#160;ms for
///         brotli (15,267 bytes) and 0.62&#160;ms for gzip (15,793 bytes) per page — noise beside the render.
///         <c>SmallestSize</c> buys brotli 12,830 bytes for 100&#160;ms and is not worth it per request.
///     </para>
/// </remarks>
internal static class PageCompression
{
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes <paramref name="content" /> as the response body, compressed when possible.</summary>
    internal static async Task WriteAsync(HttpContext context, string content, bool compress)
    {
        var response = context.Response;
        if (!compress)
        {
            await response.WriteAsync(content).ConfigureAwait(false);
            return;
        }

        // The body now depends on Accept-Encoding whichever way this goes, so a shared cache must key on
        // it — including for the identity answer, or a cached raw page is handed to a brotli client and a
        // cached brotli page to a client that cannot read it. The page is no-store anyway; this is the
        // part of correctness that does not rely on every cache honouring that.
        response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);

        var encoding = Negotiate(context.Request);
        if (encoding is null || response.Headers.ContainsKey(HeaderNames.ContentEncoding))
        {
            await response.WriteAsync(content).ConfigureAwait(false);
            return;
        }

        response.Headers.ContentEncoding = encoding;

        // One pooled UTF-8 buffer rather than a StreamWriter: the document is already a single string, so
        // there is nothing to stream, and this avoids a writer and its char buffer per request.
        var buffer = ArrayPool<byte>.Shared.Rent(_utf8.GetMaxByteCount(content.Length));
        try
        {
            var length = _utf8.GetBytes(content, buffer);
            await using Stream compressed = encoding == "br"
                ? new BrotliStream(response.Body, CompressionLevel.Optimal, leaveOpen: true)
                : new GZipStream(response.Body, CompressionLevel.Optimal, leaveOpen: true);
            await compressed.WriteAsync(buffer.AsMemory(0, length), context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     <c>"br"</c> or <c>"gzip"</c>, whichever the client ranks higher (brotli on a tie), or <c>null</c>
    ///     for identity.
    /// </summary>
    /// <remarks>
    ///     Read with ASP.NET's typed header parser, as <c>Accept-Language</c> is in
    ///     <see cref="ServerCultureNegotiation" />, so quality values are honoured rather than skipped:
    ///     <c>br;q=0</c> is a refusal, and a client that ranks gzip above brotli gets gzip.
    ///     <c>ScopedAssetCompression.Negotiate</c> ignores <c>q</c>; that was a fair trade for immutable
    ///     cached assets, and there is no reason to make it for a document built per request.
    /// </remarks>
    internal static string? Negotiate(HttpRequest request)
    {
        double brotli = 0, gzip = 0;
        foreach (var entry in request.GetTypedHeaders().AcceptEncoding)
        {
            var quality = entry.Quality ?? 1d;
            if (entry.Value.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                brotli = Math.Max(brotli, quality);
            }
            else if (entry.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                gzip = Math.Max(gzip, quality);
            }
        }

        return brotli > 0 && brotli >= gzip ? "br"
            : gzip > 0 ? "gzip"
            : null;
    }
}
