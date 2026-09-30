using Microsoft.AspNetCore.Http;

namespace Rask.Hosting.Shared;

/// <summary>
///     Which of Rask's two encodings a request accepts — the one reading of <c>Accept-Encoding</c> for the page, the
///     scoped assets and a precompressed static file alike.
/// </summary>
/// <remarks>
///     Read with ASP.NET's typed header parser, so quality values are honoured rather than skipped: <c>br;q=0</c> is a
///     refusal, and a client that ranks gzip above brotli gets gzip. The precompressed-file middleware used a substring
///     check that served brotli to a client refusing it.
/// </remarks>
internal static class ContentEncodingNegotiation
{
    /// <summary>
    ///     <c>"br"</c> or <c>"gzip"</c>, whichever the client ranks higher (brotli on a tie), or <c>null</c> for identity.
    /// </summary>
    public static string? Negotiate(HttpRequest request)
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

        if (brotli > 0 && brotli >= gzip)
        {
            return "br";
        }

        return gzip > 0 ? "gzip" : null;
    }
}
