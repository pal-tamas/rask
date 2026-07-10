using System.Text;
using Rask.Core.ScopedAssets;

namespace Rask.Native;

/// <summary>
///     The native app origin's asset table — the request-routing a native head's WebView scheme handler
///     (iOS <c>WKUrlSchemeHandler</c> / Android <c>WebViewClient.ShouldInterceptRequest</c>) needs to serve
///     a real Rask app, not just the two boot files. Given an origin-relative request path it returns the
///     bytes + content type to serve:
///     <list type="bullet">
///         <item>the boot shell (<c>/</c>) + client (<c>*/rask.native.js</c>) from <see cref="NativeClientAssets" />,</item>
///         <item>scoped CSS/JS (<c>/_rask/a/{hash}.{ext}</c>) from <see cref="ScopedAssetRegistry" />,</item>
///         <item>everything else (your <c>wwwroot</c> static files, Bootstrap <c>/_content/…</c>,
///         <c>data/*.json</c>) via the caller's <c>readStaticFile</c> reader — typically the app's bundled
///         assets on-device (Android <c>AssetManager</c>, iOS <c>NSBundle</c>).</item>
///     </list>
///     Returns <see langword="null" /> when nothing matches, so each caller picks its own fallback (a
///     WebView interceptor serves an empty 200 so the page never hangs; the <see cref="NativeAssetHttpHandler" />
///     404s). Pair it with <see cref="NativeAssetHttpHandler" /> so the in-process demo <c>HttpClient</c>
///     resolves the same origin.
/// </summary>
public static class NativeOriginAssets
{
    /// <summary>Resolves an origin-relative request path to the asset to serve, or <see langword="null" />.</summary>
    /// <param name="absolutePath">The request's absolute path (e.g. <c>/global.css</c>), no query string.</param>
    /// <param name="readStaticFile">
    ///     Reads a static file by its origin-relative key (leading <c>/</c> stripped, e.g.
    ///     <c>_content/Rask.Bootstrap/dist/css/bootstrap.min.css</c>, <c>global.css</c>, <c>data/posts-1.json</c>);
    ///     returns the bytes, or <see langword="null" /> if the file does not exist.
    /// </param>
    public static (byte[] Body, string ContentType)? Resolve(string absolutePath, Func<string, byte[]?> readStaticFile)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(readStaticFile);

        // The boot shell. Device heads load it at "/index.native.html" (WKWebView / Android WebView);
        // "/" and "/index.html" cover a host that loads the origin root instead.
        if (absolutePath is "/" or "/index.html" or "/index.native.html")
        {
            return (Encoding.UTF8.GetBytes(NativeClientAssets.IndexHtml), "text/html");
        }

        if (absolutePath.EndsWith("/rask.native.js", StringComparison.Ordinal))
        {
            return (Encoding.UTF8.GetBytes(NativeClientAssets.ClientJs), "text/javascript");
        }

        if (absolutePath.StartsWith("/_rask/a/", StringComparison.Ordinal) && TryResolveScopedAsset(absolutePath, out var scoped))
        {
            return scoped;
        }

        var rel = absolutePath.TrimStart('/');
        if (rel.Length == 0)
        {
            return null;
        }

        var bytes = readStaticFile(rel);
        return bytes is null ? null : (bytes, ContentTypeFor(rel));
    }

    private static bool TryResolveScopedAsset(string path, out (byte[] Body, string ContentType) asset)
    {
        asset = default;
        var file = path["/_rask/a/".Length..];
        var dot = file.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        var hash = file[..dot];
        var ext = file[(dot + 1)..];
        var scoped = ScopedAssetRegistry.GetByHash(hash, ext == "css" ? AssetKind.Css : AssetKind.Js);
        if (scoped is not { } a)
        {
            return false;
        }

        asset = (a.Utf8.ToArray(), ext == "css" ? "text/css" : "text/javascript");
        return true;
    }

    /// <summary>Maps a file path/key to a content type by extension.</summary>
    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".vtt" => "text/vtt",
        ".html" => "text/html",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };
}
