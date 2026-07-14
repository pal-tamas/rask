using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Client.Browser;

/// <summary>
///     Registration for the in-process browser wrappers shared by the WASM and Native hosts (not Server): the
///     ones that need transient user activation the WebSocket round-trip would lose. Today that is
///     <see cref="IShare" />; a Native platform head can register a native backend first, which then wins over
///     this JS-backed default (see <see cref="RaskBrowserApis.AddBrowserApi{TService,TImpl}" />).
/// </summary>
public static class RaskClientBrowserApis
{
    /// <summary>Registers the in-process (WASM + Native) browser wrappers at <paramref name="lifetime" />.</summary>
    public static IServiceCollection AddClientBrowserApis(this IServiceCollection services, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBrowserApi<IShare, Share>(lifetime);
        return services;
    }
}
