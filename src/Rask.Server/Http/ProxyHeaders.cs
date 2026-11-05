using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Server.Http;

/// <summary>
///     What a proxy in front may say about the request, for every Rask server host: <c>RaskApp</c> and one wired by
///     hand with <c>AddRask()</c>/<c>MapRask()</c> alike. Opt-in by <c>Rask:BehindProxy</c> (which <c>rask deploy</c>
///     sets, and <c>RaskAppOptions.BehindProxy</c> writes), because trusting these headers from an arbitrary client
///     lets it forge its own IP.
/// </summary>
internal static class ProxyHeaders
{
    /// <summary>The setting that says a proxy is in front.</summary>
    public const string Setting = "Rask:BehindProxy";

    /// <summary>
    ///     The headers trusted, from any sender. ASP.NET trusts only a LOOPBACK sender by default, and the proxy
    ///     <c>rask deploy</c> puts in front (Caddy) runs in another container, so its headers were dropped: the scheme
    ///     stayed <c>http</c> and the client was the proxy. Turning the setting on already states that every request
    ///     comes through a proxy (an app exposed directly must leave it off), so the sender is trusted wherever it is.
    /// </summary>
    public static ForwardedHeadersOptions Options()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return options;
    }

    /// <summary>Puts the headers' middleware at the very front of the pipeline, once, when the setting is on.</summary>
    public static void Register(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, Startup>());

    /// <summary>
    ///     A startup filter's middleware runs before anything <c>Program.cs</c> adds, so HSTS, redirects, the error
    ///     handler and the app's own logging all see the request the visitor actually made.
    /// </summary>
    private sealed class Startup(IConfiguration configuration) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (configuration.GetValue<bool>(Setting))
            {
                app.UseForwardedHeaders(Options());
            }

            next(app);
        };
    }
}
