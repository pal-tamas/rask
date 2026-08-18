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
///     the cache headers, and the content-type mapping, all now expressed through
///     <see cref="ScopedAssetBundle" /> so the two copies cannot drift apart.
/// </summary>
internal static class RaskAssetEndpoint
{
    private static readonly string[] _methods = ["GET", "HEAD"];

    public static void MapRaskAssets(IEndpointRouteBuilder endpoints, string pathBase)
    {
        // A wasm-hosted app that also mounts the server-rendered dashboard calls both hosts' UseRask,
        // and both want this route. Two endpoints with an identical template and identical precedence
        // are not an error at startup — routing throws AmbiguousMatchException on the first request
        // for a scoped stylesheet, so the app boots clean and then serves an unstyled 500. Mapping at
        // most once is what prevents that; because both handlers resolve through ScopedAssetBundle,
        // whichever host wins the race serves the same bytes.
        if (RaskEndpointMap.IsMapped(endpoints, pathBase + "/_rask/a/{hash}.css"))
        {
            return;
        }

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
        if (!ScopedAssetBundle.IsContentHash(hash))
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
            return ServeBakedFileAsync(ctx, hash, kind);
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";

        var contentType = ScopedAssetBundle.ContentType(kind);

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
    // registry doesn't carry the hash. Negotiates a precompressed .br/.gz sibling when present (the
    // WASM publish bakes them next to the asset), matching the in-registry serving path.
    private static async Task ServeBakedFileAsync(HttpContext ctx, string hash, AssetKind kind)
    {
        if (ScopedAssetBundle.FindBakedFile(hash, kind) is not { } path)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";
        ctx.Response.Headers.ETag = "\"" + hash + "\"";
        ctx.Response.ContentType = ScopedAssetBundle.ContentType(kind);

        var encoding = ScopedAssetCompression.Negotiate(ctx.Request.Headers.AcceptEncoding.ToString());
        if (ScopedAssetBundle.FindPrecompressedSibling(path, encoding) is { } sibling)
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            await ctx.Response.SendFileAsync(sibling);
            return;
        }

        await ctx.Response.SendFileAsync(path);
    }
}

/// <summary>
///     Whether a route template is already on an <see cref="IEndpointRouteBuilder" />.
///     <para>
///         Deliberately a six-line duplicate of the identical helper in <c>Rask.Server</c> rather than a
///         shared one in <c>Rask.Core</c>: the check needs <c>RoutePattern</c>, and Core takes no
///         ASP.NET routing dependency. The contract it encodes — a framework endpoint is mapped at most
///         once per app, whichever host gets there first — lives in both copies' comments.
///     </para>
/// </summary>
internal static class RaskEndpointMap
{
    public static bool IsMapped(IEndpointRouteBuilder endpoints, string rawTemplate)
    {
        foreach (var source in endpoints.DataSources)
        {
            foreach (var endpoint in source.Endpoints)
            {
                if (endpoint is RouteEndpoint route
                    && string.Equals(route.RoutePattern.RawText, rawTemplate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
