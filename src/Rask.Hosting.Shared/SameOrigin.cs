using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Hosting.Shared;

/// <summary>
/// Whether a request came from a page this host served — the check that keeps another site from driving
/// a cookie-authenticated endpoint, or opening a WebSocket, as the visitor.
/// </summary>
internal static class SameOrigin
{
    /// <summary>The app's public origin(s), comma-separated: <c>https://app.example.com</c>.</summary>
    public const string PublicOriginSetting = "Rask:PublicOrigin";

    /// <summary>The auth battery's public origin, which <c>rask deploy --domain</c> sets; read when the general one is unset.</summary>
    public const string AuthPublicOriginSetting = "Rask:Auth:PublicOrigin";

    /// <summary>
    /// True when the request carries no <c>Origin</c>/<c>Referer</c> (a non-browser client, or a
    /// same-origin fetch that omits it), or one that is the request's own origin — the same scheme, host
    /// and port — or one of the app's configured public origins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host alone was not enough: another app on the same machine — <c>http://app:8081</c> beside
    /// <c>https://app</c>, a dev server on another port — is another origin, and was let in.
    /// </para>
    /// <para>
    /// Behind a proxy the request is compared as the proxy says it was made when its headers are trusted
    /// (<c>Rask:BehindProxy</c>, ForwardedHeaders). A proxy that says too little — TLS ended in front, a
    /// <c>Host</c> rewritten — is named instead: <c>Rask:PublicOrigin</c> (or the auth battery's
    /// <c>Rask:Auth:PublicOrigin</c>) lists the origins the app's pages are served from, and a browser on one
    /// of them is let in whatever the request looks like behind it.
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

        return IsRequestsOwn(request, originUri) || IsPublic(request, originUri);
    }

    private static bool IsRequestsOwn(HttpRequest request, Uri origin)
    {
        // request.Host.Host is the host without the port (empty if the Host header is missing/malformed).
        var selfHost = request.Host.Host;
        if (string.IsNullOrEmpty(selfHost)
            || !string.Equals(origin.Host, selfHost, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var selfPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return origin.Port == selfPort;
    }

    private static bool IsPublic(HttpRequest request, Uri origin)
    {
        var configuration = request.HttpContext.RequestServices?.GetService<IConfiguration>();
        var configured = configuration?[PublicOriginSetting] is { Length: > 0 } general
            ? general
            : configuration?[AuthPublicOriginSetting];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        foreach (var entry in configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var allowed)
                && string.Equals(allowed.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(allowed.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
                && allowed.Port == origin.Port)
            {
                return true;
            }
        }

        return false;
    }
}
