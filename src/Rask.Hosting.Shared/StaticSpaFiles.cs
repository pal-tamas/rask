using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rask.Hosting.Shared;

/// <summary>
///     The serving details every Rask host that puts a built single-page app behind ASP.NET has in
///     common — the WASM bundle host and the JS-framework host.
/// </summary>
/// <remarks>
///     Source-linked into both rather than shared through a package: neither host should depend on
///     the other, and a third package existing only to hold two helpers would be worse than the
///     duplication it removes.
///     <para>
///         Cache classification is deliberately <em>not</em> here. The two hosts disagree about what
///         a fingerprinted filename looks like — the WASM SDK writes <c>dotnet.7a8b9c2d3e.js</c>
///         (dot-separated, lowercase hex) while Vite writes <c>index-DkK9xYz1.js</c> (dash-separated,
///         mixed case) — and a rule wide enough for both would mark <c>vendor-react.js</c> immutable
///         for a year. A wrong <c>immutable</c> cannot be taken back without renaming the file, so
///         each host owns its own rule.
///     </para>
/// </remarks>
internal static class StaticSpaFiles
{
    /// <summary>
    ///     The name of the asset a request is really for, with any precompressed-sibling suffix
    ///     removed — <c>app.css.br</c> is still a stylesheet.
    /// </summary>
    /// <remarks>
    ///     <see cref="PrecompressedFileMiddleware" /> rewrites the request path to the sibling, after
    ///     which the static-file middleware keys the content type off <c>.br</c>. That extension is
    ///     unknown to it, so the response lands on the octet-stream default and the browser refuses
    ///     to run the script or apply the stylesheet.
    /// </remarks>
    public static string UnderlyingFileName(string fileName) =>
        fileName.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3]
            : fileName;

    /// <summary>
    ///     Maps a catch-all fallback at the root, or scoped under <paramref name="pathBase" /> so two
    ///     bundles can live in one host.
    /// </summary>
    /// <remarks>
    ///     The prefixed shape carries <c>{*path:nonfile}</c>, which is what keeps a request for a
    ///     missing asset a 404 instead of a page of HTML — handing a browser <c>index.html</c> for a
    ///     module import produces a decode error that reads as a broken framework. The root shape is
    ///     deliberately unconstrained, preserving the behaviour the WASM host shipped with.
    /// </remarks>
    public static void MapCatchAll(IEndpointRouteBuilder endpoints, string pathBase, RequestDelegate handler)
    {
        if (pathBase.Length == 0)
        {
            endpoints.MapFallback(handler);
            return;
        }

        endpoints.MapFallback(pathBase.TrimStart('/') + "/{*path:nonfile}", handler);
    }
}
