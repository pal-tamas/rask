using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Meta.Hosting;

/// <summary>
///     Puts the meta framework's Node server behind this app: everything Kestrel does not answer
///     itself is forwarded to it.
/// </summary>
public static class RaskMetaEndpointExtensions
{
    /// <summary>
    ///     Forwards unmatched requests to the framework's Node server.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Map your API before calling this.</b> <c>MapRaskCqrs()</c>, any minimal APIs and any
    ///         health checks must be mapped first. What is registered here is a fallback, so a literal
    ///         route still wins — but the ordering is the contract rather than a coincidence, and the
    ///         symptom of getting it wrong is an API call answered with a rendered HTML page.
    ///     </para>
    ///     <para>
    ///         Requires <see cref="RaskMetaServiceCollectionExtensions.AddRaskMeta" />, which is what
    ///         starts and supervises the process this forwards to.
    ///     </para>
    /// </remarks>
    /// <param name="endpoints">The app's endpoint route builder.</param>
    /// <returns><paramref name="endpoints" />, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>AddRaskMeta()</c> was not called.</exception>
    public static IEndpointRouteBuilder UseRaskMeta(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var forwarder = endpoints.ServiceProvider.GetService<NodeForwarder>()
            ?? throw new InvalidOperationException(
                "UseRaskMeta() requires AddRaskMeta() on the service collection.");

        endpoints.MapFallback(forwarder.ForwardAsync);

        return endpoints;
    }
}
