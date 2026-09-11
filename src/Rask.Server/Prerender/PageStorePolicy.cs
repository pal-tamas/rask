using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Rask.Server.Prerender;

/// <summary>What happens to the stored copy of a page after it is rendered again.</summary>
internal enum PageStoreDecision
{
    /// <summary>The render replaces the stored copy.</summary>
    Store,

    /// <summary>
    ///     The render is not stored, but a copy already stored is kept and served — the failure looks
    ///     transient, and a good page should not disappear because one render of it went wrong.
    /// </summary>
    KeepStale,

    /// <summary>The page is no longer one that can be shared; any stored copy goes.</summary>
    Evict,
}

/// <summary>A decision about a render, and the reason it is given in the log.</summary>
/// <param name="Decision">What happens to the stored copy.</param>
/// <param name="Reason">Why, phrased to finish "served live, not cached — ". Empty when stored.</param>
internal readonly record struct PageStoreVerdict(PageStoreDecision Decision, string Reason);

/// <summary>
///     Decides whether a render of a public page may become the copy served to everyone.
/// </summary>
/// <remarks>
///     <para>
///         The renders this judges are neutral — made for nobody, with no request behind them — so what is
///         left to decide is whether the render is <em>finished</em> and <em>ordinary</em>. A render still
///         waiting on work serves a placeholder, and a baked spinner is worse than no copy, because it
///         looks like a page. A redirect or an error status is a fact about this moment, not a page.
///     </para>
///     <para>
///         Transient and definitive failures are told apart on purpose. A page that threw or stalled
///         keeps the copy it had, because an outage should not erase good content; a page that now
///         answers 404 or redirects has said it is gone, and serving the old copy would contradict it.
///     </para>
/// </remarks>
internal static class PageStorePolicy
{
    /// <summary>
    ///     Judges <paramref name="render" /> of a public page.
    /// </summary>
    /// <param name="render">The neutral render.</param>
    /// <param name="document">
    ///     The render shaped for storing — <c>null</c> when it cannot be served without a live session.
    /// </param>
    /// <param name="documentBytes">The UTF-8 size of <paramref name="document" />.</param>
    /// <param name="maxEntryBytes">The largest copy the cache keeps.</param>
    internal static PageStoreVerdict Decide(
        in PageRenderResult render,
        string? document,
        int documentBytes,
        long maxEntryBytes)
    {
        if (render.Kind == PageRenderKind.Redirect)
        {
            return new PageStoreVerdict(PageStoreDecision.Evict, "it redirects");
        }

        if (render.Faulted)
        {
            return new PageStoreVerdict(PageStoreDecision.KeepStale, "its render threw");
        }

        if (render.TimedOut)
        {
            return new PageStoreVerdict(PageStoreDecision.KeepStale, "it did not settle within the quiescence budget");
        }

        if (render.BlockedOnJs)
        {
            return new PageStoreVerdict(PageStoreDecision.KeepStale, "its render is waiting on JavaScript");
        }

        if (render.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return new PageStoreVerdict(PageStoreDecision.KeepStale, AnsweredWith(render.StatusCode));
        }

        if (render.StatusCode != StatusCodes.Status200OK)
        {
            return new PageStoreVerdict(PageStoreDecision.Evict, AnsweredWith(render.StatusCode));
        }

        // A neutral render is made for nobody, so being authenticated afterwards means the render itself
        // signed someone in. Whatever that page shows now belongs to them.
        if (render.Authenticated)
        {
            return new PageStoreVerdict(PageStoreDecision.Evict, "its render signs someone in");
        }

        if (document is null)
        {
            return new PageStoreVerdict(
                PageStoreDecision.Evict,
                render.NeedsSession
                    ? "it needs a live session"
                    : "its runtime script could not be removed safely");
        }

        if (documentBytes > maxEntryBytes)
        {
            return new PageStoreVerdict(
                PageStoreDecision.Evict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"it is {documentBytes} bytes, more than the {maxEntryBytes} one copy may take"));
        }

        return new PageStoreVerdict(PageStoreDecision.Store, string.Empty);
    }

    private static string AnsweredWith(int statusCode) =>
        string.Create(CultureInfo.InvariantCulture, $"it answered {statusCode}");
}
