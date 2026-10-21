using System.Globalization;
using System.Security.Claims;
using Rask.Core.Routing;

namespace Rask.Core.Printing;

/// <summary>
/// The server host's pages as a printer reads them: a route or a component rendered as a first GET would render
/// it — static HTML, no live session left behind — for a given user, and the app's own static files beside it.
/// </summary>
/// <remarks>Declared here because Rask.Pdf cannot reference Rask.Server; Rask.Server implements and registers it.</remarks>
internal interface IPrintablePages
{
    /// <summary>
    /// Renders <paramref name="route"/> for <paramref name="user"/>, after the route's <c>[Authorize]</c> admits them, in
    /// <paramref name="culture"/> when the app ships it and in its default otherwise (null: the default).
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The route's authorization refuses <paramref name="user"/>.</exception>
    /// <exception cref="InvalidOperationException">No page answers there, or it redirected, or it failed to render.</exception>
    Task<PrintablePage> Render(RouteUrl route, ClaimsPrincipal user, CultureInfo? culture, CancellationToken cancellation);

    /// <summary>Renders <paramref name="component"/> as a whole document of the app, for <paramref name="user"/>.</summary>
    Task<PrintablePage> Render(Component component, ClaimsPrincipal user, CultureInfo? culture, CancellationToken cancellation);

    /// <summary>The static file the app serves at <paramref name="path"/>, or null when it serves none there.</summary>
    ValueTask<PrintableFile?> File(string path, CancellationToken cancellation);
}
