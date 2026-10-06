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
    /// same-origin fetch that omits it) or one whose host is the request's own.
    /// </summary>
    /// <remarks>
    /// Host only — not scheme or port. Behind a TLS-terminating reverse proxy the browser's Origin is
    /// <c>https://app</c> (:443) while the request's scheme and host can be http (:80) unless
    /// ForwardedHeaders is wired, so a scheme or port match would refuse a legitimate visitor.
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
        return !string.IsNullOrEmpty(selfHost)
               && string.Equals(originUri.Host, selfHost, StringComparison.OrdinalIgnoreCase);
    }
}
