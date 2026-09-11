using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rask.Server.DevTools;

/// <summary>
///     What <c>Rask.DevTools</c> registers so a Server host serves the devtools without referencing the package.
/// </summary>
/// <remarks>
///     The devtools bootstrap only ever sees an <c>IServiceCollection</c> — <c>AddRask</c> attaches it — but what it
///     serves needs the route builder and the request, which exist only once <c>UseRask</c> runs. So the bootstrap
///     registers this and the host calls it at the four points that matter. It is present exactly when the devtools
///     attached, which already took a build carrying them with their feature switch on; whether they serve anything is
///     still the implementation's call (Development only).
/// </remarks>
internal interface IRaskServerDevTools
{
    /// <summary>Maps the devtools' endpoints under <paramref name="pathBase" />. Called once per app.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, string pathBase);

    /// <summary>
    ///     The host script a live page for <paramref name="context" /> loads, with the panel page it frames for
    ///     <paramref name="sessionId" />; or null when the page loads no devtools. Called per page request, after
    ///     <see cref="MapEndpoints" />.
    /// </summary>
    DevToolsPageTag? PageTag(HttpContext context, string sessionId);

    /// <summary>
    ///     The status to refuse a request for a devtools page with, or null to serve it — including for every path the
    ///     devtools do not own. Called by the page handler after route authorization and before a session is created,
    ///     so a refused request costs no session.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="path">The request path with the app's path base removed.</param>
    int? RefusePanel(HttpContext context, string path);

    /// <summary>
    ///     Whether a session last seen at <paramref name="path" /> (path base removed) may be rebuilt from a resume
    ///     record. False for the devtools' own pages: their access is decided per request, and a resume record carries
    ///     none of what that decision reads.
    /// </summary>
    bool CanResume(string path);
}

/// <summary>What a page's devtools script tag names: the host script, and the panel page it frames.</summary>
internal sealed record DevToolsPageTag(string ScriptUrl, string PanelUrl);
