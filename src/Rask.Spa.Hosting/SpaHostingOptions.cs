using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Rask.Spa.Hosting;

/// <summary>
///     Configures <see cref="RaskSpaEndpointExtensions.UseRaskSpa" />. Every default is the one a
///     Vite-built app wants; each property is a deliberate departure from it.
/// </summary>
public sealed class SpaHostingOptions
{
    /// <summary>The bundle's entry document, served for every client-side route.</summary>
    public string IndexFileName { get; set; } = "index.html";

    /// <summary>
    ///     Request-path prefixes whose contents the bundler content-hashes, and which may therefore be
    ///     cached for ever.
    /// </summary>
    /// <remarks>
    ///     This is the primary cache rule because it is a bundler <em>guarantee</em> rather than a guess
    ///     about filenames: Vite writes everything under <c>build.assetsDir</c> (default
    ///     <c>assets</c>) with a content hash in the name. Angular emits at the dist root instead, which
    ///     is what the filename heuristic exists to cover.
    ///     <para>
    ///         These prefixes also never fall back to the index document — a request for a missing file
    ///         under one of them is a 404, not a page of HTML.
    ///     </para>
    /// </remarks>
    public IList<string> ImmutablePathPrefixes { get; } = new List<string> { "/assets/" };

    /// <summary>
    ///     Whether to serve a <c>.br</c>/<c>.gz</c> sibling when one sits next to the requested file.
    ///     Costs nothing when the bundler emits none.
    /// </summary>
    public bool ServePrecompressed { get; set; } = true;

    /// <summary>
    ///     Decides whether a request that matched no file and no endpoint should be refused with a 404
    ///     rather than answered with the index document. <c>null</c> uses the built-in rule: anything
    ///     under <see cref="ImmutablePathPrefixes" />, and anything whose <c>Accept</c> header asks for
    ///     something other than HTML.
    /// </summary>
    /// <remarks>
    ///     Handing a browser <c>index.html</c> for a missing module import produces
    ///     <c>Failed to load module script</c>, which reads as a broken framework rather than a missing
    ///     file — so the fallback has to be narrower than "everything".
    /// </remarks>
    public Func<HttpContext, bool>? ExcludeFromFallback { get; set; }

    /// <summary>
    ///     Where the bundler's dev server is listening, named in the message shown in Development when
    ///     no build output exists. Baked from the client's configuration by the build targets.
    /// </summary>
    public string? DevServerUrl { get; set; }

    /// <summary>
    ///     Runs after this package has set the content type and cache headers, so an app can override
    ///     either.
    /// </summary>
    public Action<StaticFileResponseContext>? OnPrepareResponse { get; set; }
}
