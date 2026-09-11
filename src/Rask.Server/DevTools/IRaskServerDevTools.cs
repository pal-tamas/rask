using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rask.Server.DevTools;

/// <summary>
///     What <c>Rask.DevTools</c> registers so a Server host maps the devtools' endpoints without referencing
///     the package.
/// </summary>
/// <remarks>
///     The devtools bootstrap only ever sees an <c>IServiceCollection</c> — <c>AddRask</c> attaches it — but
///     its endpoints need the route builder, which exists only once <c>UseRask</c> runs. So the bootstrap
///     registers this and <c>UseRask</c> hands it the endpoints. It is present exactly when the devtools
///     attached, which already took a build carrying them with their feature switch on; whether they serve
///     anything is still the implementation's call (Development only).
/// </remarks>
internal interface IRaskServerDevTools
{
    /// <summary>Maps the devtools' endpoints under <paramref name="pathBase" />. Called once per app.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, string pathBase);

    /// <summary>
    ///     The host script URL a live page serving <paramref name="context" /> should name, or null when the
    ///     page loads no devtools. Called per page request, after <see cref="MapEndpoints" />.
    /// </summary>
    string? HostScriptUrl(HttpContext context);
}
