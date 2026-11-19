using Microsoft.AspNetCore.Http;

namespace Rask.Hosting.Shared;

/// <summary>
/// Whether a request came from a page this host served — the check that keeps another site from driving
/// a cookie-authenticated endpoint, or opening a WebSocket, as the visitor.
/// </summary>
internal static class SameOrigin
{
    /// <summary>
    /// True when the request carries no <c>Origin</c>/<c>Referer</c> (a non-browser client, or a
    /// same-origin fetch that omits it) or one that is the request's own: the same host, the same port
    /// where the request names one, and not plain http for a request that came over https.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host alone was not enough: another app on the same machine — <c>http://app:8081</c> beside
    /// <c>https://app</c>, a dev server on another port — is another origin, and was let in.
    /// </para>
    /// <para>
    /// Still lenient where a proxy hides the truth. Behind a TLS-terminating reverse proxy the browser's
    /// Origin is <c>https://app</c> (:443) while the request arrives as http with a <c>Host</c> of
    /// <c>app</c> unless ForwardedHeaders is wired. So the port is compared only when the request's
    /// <c>Host</c> names one, and the scheme only when the request itself is https — an https request is
    /// never sent from an http page of ours, and a refused http one would be a legitimate visitor.
    /// </para>
    /// </remarks>
    public static bool Allows(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            var referer = request.Headers.Referer.ToString();
            if (string.IsNullOrEmpty(referer))
            {
                return true;
            }

            origin = referer;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        // request.Host.Host is the host without the port (empty if the Host header is missing/malformed).
        var selfHost = request.Host.Host;
        if (string.IsNullOrEmpty(selfHost)
            || !string.Equals(originUri.Host, selfHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.IsHttps && !string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return request.Host.Port is not { } port || originUri.Port == port;
    }
}
