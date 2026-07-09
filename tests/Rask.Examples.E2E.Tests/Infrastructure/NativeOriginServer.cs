using Microsoft.Playwright;
using Rask.Core.ScopedAssets;
using Rask.Native;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the native app origin to the headless page — the E2E stand-in for a device head's scheme
///     handler (<c>WKUrlSchemeHandler</c> / <c>WebViewAssetLoader</c>). Installed as a catch-all
///     <c>page.RouteAsync("**/*", …)</c> so every request the shell makes is fulfilled from managed
///     sources rather than the network:
///     <list type="bullet">
///         <item>the boot shell + client from <see cref="NativeClientAssets" />,</item>
///         <item>scoped CSS/JS (<c>/_rask/a/{hash}.{ext}</c>) from <see cref="ScopedAssetRegistry" />,</item>
///         <item>the sample's own static files (<c>global.css</c>, <c>img/*</c>, <c>data/*</c>) from its
///         <c>wwwroot</c>, and Bootstrap (<c>/_content/Rask.Bootstrap/*</c>) from the package sources,</item>
///         <item>anything else (e.g. the Google Fonts CDN) fulfilled empty so the page never hangs on a
///         real network fetch.</item>
///     </list>
///     Note: the demo <c>HttpClient</c> runs in the .NET host, not the browser, so its fetches do NOT pass
///     through here — the native journey avoids the pages that depend on it.
/// </summary>
internal static class NativeOriginServer
{
    private static readonly string RepoRoot = LocateRepoRoot();
    private static readonly string NativeWwwroot =
        Path.Combine(RepoRoot, "samples", "Rask.Example.Native", "wwwroot");
    private static readonly string BootstrapWwwroot =
        Path.Combine(RepoRoot, "src", "Rask.Bootstrap", "wwwroot");

    public static async Task HandleAsync(IRoute route)
    {
        var path = new Uri(route.Request.Url).AbsolutePath;

        if (path is "/" or "/index.html")
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "text/html",
                Body = NativeClientAssets.IndexHtml
            });
            return;
        }

        if (path.EndsWith("/rask.native.js", StringComparison.Ordinal))
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "text/javascript",
                Body = NativeClientAssets.ClientJs
            });
            return;
        }

        if (path.StartsWith("/_rask/a/", StringComparison.Ordinal) && TryServeScopedAsset(path, out var scoped))
        {
            await route.FulfillAsync(scoped);
            return;
        }

        // Static files: sample wwwroot (global.css / img / data) and Bootstrap package assets.
        var file = ResolveStaticFile(path);
        if (file is not null && File.Exists(file))
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = ContentTypeFor(file),
                BodyBytes = await File.ReadAllBytesAsync(file)
            });
            return;
        }

        // Unknown (fonts CDN, favicons we don't ship, …) — empty 200 so nothing blocks the render.
        await route.FulfillAsync(new RouteFulfillOptions { Status = 200, Body = "" });
    }

    private static bool TryServeScopedAsset(string path, out RouteFulfillOptions options)
    {
        options = default!;
        var file = path["/_rask/a/".Length..];
        var dot = file.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        var hash = file[..dot];
        var ext = file[(dot + 1)..];
        var asset = ScopedAssetRegistry.GetByHash(hash, ext == "css" ? AssetKind.Css : AssetKind.Js);
        if (asset is not { } a)
        {
            return false;
        }

        options = new RouteFulfillOptions
        {
            ContentType = ext == "css" ? "text/css" : "text/javascript",
            BodyBytes = a.Utf8.ToArray()
        };
        return true;
    }

    private static string? ResolveStaticFile(string path)
    {
        // Strip any cache-busting query already removed by AbsolutePath; map by prefix.
        if (path.StartsWith("/_content/Rask.Bootstrap/", StringComparison.Ordinal))
        {
            return Path.Combine(BootstrapWwwroot,
                path["/_content/Rask.Bootstrap/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        // Everything else under the origin root maps into the sample's wwwroot (global.css, img/*, data/*).
        var rel = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return rel.Length == 0 ? null : Path.Combine(NativeWwwroot, rel);
    }

    private static string ContentTypeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
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

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
