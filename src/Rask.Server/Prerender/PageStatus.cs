using Microsoft.AspNetCore.Http;

namespace Rask.Server.Prerender;

/// <summary>The status a rendered page answers with.</summary>
internal static class PageStatus
{
    /// <summary>
    ///     The status a rendered page answers with.
    /// </summary>
    /// <param name="faulted">Whether the root boundary rendered its error document.</param>
    /// <param name="declaredStatus">What the page set through <c>IPageResponse.SetStatus</c>, if anything.</param>
    /// <param name="notFoundMounted">Whether the render actually mounted the not-found page.</param>
    /// <remarks>
    ///     <para>
    ///         A page that threw does not get to claim it succeeded — the error document is what is being
    ///         served (#607). Below that, a page's own status wins, so a page can deliberately answer 200
    ///         where the router would say 404 (a soft 404).
    ///     </para>
    ///     <para>
    ///         The not-found status is gated on the page actually having been MOUNTED, not merely resolved:
    ///         an app that renders its root directly resolves the fallback too, and 404-ing every path such
    ///         an app serves would be a far worse lie than the one being fixed.
    ///     </para>
    /// </remarks>
    internal static int Of(bool faulted, int? declaredStatus, bool notFoundMounted)
    {
        if (faulted)
        {
            return StatusCodes.Status500InternalServerError;
        }

        return declaredStatus ?? (notFoundMounted ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
    }
}
