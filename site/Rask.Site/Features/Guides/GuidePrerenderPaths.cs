using Rask.Core.Live;

namespace Rask.Site.Features;

/// <summary>
///     Tells the prerender pass what <c>/docs/guides/{slug}</c> expands to.
/// </summary>
/// <remarks>
///     <para>
///         The guides ARE the site. Without this the pass writes the twenty pages around them and skips
///         the route that holds ~80 documents, so the whole of the site's content ships to a crawler as
///         a boot shell — and the publish log reads as a success, because everything it knew about was
///         written.
///     </para>
///     <para>
///         Built from <see cref="GuideCatalog.All" />, which is the same list the index cards and the
///         sidebar are built from, so a guide added to the catalog is prerendered, listed in the sitemap
///         and linked from the index by one edit rather than three.
///     </para>
/// </remarks>
public sealed class GuidePrerenderPaths : IPrerenderPaths
{
    // The cast is the RouteUrl -> string conversion, spelled out because Select cannot infer it: the
    // generated helper returns a RouteUrl so that a call site which forgets a parameter is a compile
    // error rather than a URL with a brace in it.
    public IEnumerable<string> Paths() =>
        GuideCatalog.All.Select(guide => (string)Routes.GuidePage(guide.Slug));
}
