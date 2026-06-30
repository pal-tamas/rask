using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Rask.Core.ScopedAssets;

namespace Rask.Wasm.Hosting;

/// <summary>
///     Per-component content-addressed asset endpoint for a WASM-hosting ASP.NET app.
///     Mirrors the Server-side <c>RaskEndpointExtensions.ServeAssetAsync</c> exactly so
///     a WASM client and a Server client see byte-identical responses for the same hash.
///     Kept as a small duplicate (rather than a shared abstraction) to avoid adding an
///     ASP.NET dependency to <c>Rask.Core</c> — the shared invariant is the URL pattern,
///     the cache headers, and the content-type mapping, all documented inline.
/// </summary>
internal static class RaskAssetEndpoint
{
    private static readonly string[] _methods = ["GET", "HEAD"];

    public static void MapRaskAssets(IEndpointRouteBuilder endpoints, string pathBase, string? bundleDir = null)
    {
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.css", _methods,
                ctx => ServeAsync(ctx, AssetKind.Css, bundleDir))
            .AllowAnonymous();
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js", _methods,
                ctx => ServeAsync(ctx, AssetKind.Js, bundleDir))
            .AllowAnonymous();
    }

    private static Task ServeAsync(HttpContext ctx, AssetKind kind, string? bundleDir)
    {
        var hash = ctx.Request.RouteValues["hash"] as string;
        if (string.IsNullOrEmpty(hash) || !IsLowercaseHex(hash, ScopedAssetRegistry.HashHexLength))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var bytes = ScopedAssetRegistry.GetByHash(hash, kind);
        if (bytes is null)
        {
            // Registry miss. This host serves a PUBLISHED WASM bundle, so the authoritative copy is the
            // baked /_rask/a/{hash}.{ext} file the publish wrote (hashed from the full in-WASM-runtime
            // set). The in-process registry only carries assets from assemblies this host process loaded,
            // which can be a strict subset (e.g. when the App's referenced UI packages aren't touched on
            // the host), so its hash for the single concatenated bundle won't match the browser's request.
            // Because routing matched this endpoint, UseStaticFiles was skipped and can't serve the baked
            // file — so serve it here instead of shadowing it with a 404.
            return ServeBakedFileAsync(ctx, hash, kind, bundleDir);
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";

        var contentType = kind == AssetKind.Css
            ? "text/css; charset=utf-8"
            : "text/javascript; charset=utf-8";

        // Negotiate br/gzip via the shared Core helper so a WASM-hosting client sees byte-identical
        // compressed responses to the Server one. Compressed reps are cached (content-addressed).
        var encoding = ScopedAssetCompression.Negotiate(ctx.Request.Headers.AcceptEncoding.ToString());
        if (encoding is not null
            && ScopedAssetCompression.GetEncoded(hash, kind, encoding) is { } enc)
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            return Results.Bytes(enc.Bytes, contentType,
                    entityTag: new EntityTagHeaderValue(enc.Etag))
                .ExecuteAsync(ctx);
        }

        return Results.Bytes(
                bytes.Value.Utf8.ToArray(),
                contentType,
                enableRangeProcessing: true,
                entityTag: new EntityTagHeaderValue(bytes.Value.Etag))
            .ExecuteAsync(ctx);
    }

    // Serves the baked /_rask/a/{hash}.{ext} file from the published bundle when the in-process
    // registry doesn't carry the hash. The hash is already validated as fixed-length lowercase hex, so
    // it can't traverse outside the bundle directory. Negotiates a precompressed .br/.gz sibling when
    // present (the WASM publish bakes them next to the asset), matching the in-registry serving path.
    private static async Task ServeBakedFileAsync(HttpContext ctx, string hash, AssetKind kind, string? bundleDir)
    {
        var ext = kind == AssetKind.Css ? ".css" : ".js";
        var path = bundleDir is null ? null : Path.Combine(bundleDir, "_rask", "a", hash + ext);
        if (path is null || !File.Exists(path))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";
        ctx.Response.Headers.ETag = "\"" + hash + "\"";
        ctx.Response.ContentType = kind == AssetKind.Css
            ? "text/css; charset=utf-8"
            : "text/javascript; charset=utf-8";

        var encoding = ScopedAssetCompression.Negotiate(ctx.Request.Headers.AcceptEncoding.ToString());
        var sibling = encoding switch
        {
            "br" => path + ".br",
            "gzip" => path + ".gz",
            _ => null
        };
        if (sibling is not null && File.Exists(sibling))
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            await ctx.Response.SendFileAsync(sibling);
            return;
        }

        await ctx.Response.SendFileAsync(path);
    }

    private static bool IsLowercaseHex(string s, int expectedLength)
    {
        if (s.Length != expectedLength)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
