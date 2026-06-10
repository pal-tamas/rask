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

    public static void MapRaskAssets(IEndpointRouteBuilder endpoints, string pathBase)
    {
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.css", _methods,
                ctx => ServeAsync(ctx, AssetKind.Css))
            .AllowAnonymous();
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js", _methods,
                ctx => ServeAsync(ctx, AssetKind.Js))
            .AllowAnonymous();
    }

    private static Task ServeAsync(HttpContext ctx, AssetKind kind)
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
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var contentType = kind == AssetKind.Css
            ? "text/css; charset=utf-8"
            : "text/javascript; charset=utf-8";

        return Results.Bytes(
                bytes.Value.Utf8.ToArray(),
                contentType,
                enableRangeProcessing: true,
                entityTag: new EntityTagHeaderValue(bytes.Value.Etag))
            .ExecuteAsync(ctx);
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
