using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Core.Globalization;

/// <summary>Registers the culture services both hosts need.</summary>
public static class RaskCultureServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the visitor's culture, and configures the app's languages.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">
    ///     Adds the languages this app ships. Leaving <see cref="RaskCultureOptions.SupportedCultures" />
    ///     empty — which is what happens when a host calls this on an app that never asked for
    ///     localization — keeps culture support switched off.
    /// </param>
    /// <param name="lifetime">
    ///     <see cref="ServiceLifetime.Scoped" /> on the server, where a scope is a live session and so a
    ///     visitor; <see cref="ServiceLifetime.Singleton" /> on WASM, where the whole app is one visitor.
    /// </param>
    /// <remarks>
    ///     Called unconditionally by every host, so that <c>IRaskCulture</c> can be a host contract and a
    ///     component may take it in its constructor without first asking whether localization is on.
    ///     Only <c>configure</c> adding a supported culture actually turns the subsystem on.
    /// </remarks>
    public static IServiceCollection AddRaskCulture(
        this IServiceCollection services,
        Action<RaskCultureOptions>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskCultureOptions();
        configure?.Invoke(options);

        // The options describe the APP, so they are a singleton whichever lifetime the culture takes.
        services.TryAddSingleton(options);

        // The process-wide switch that lets every read on the render and dispatch paths cost nothing
        // until an app actually configures a language. It only ever decides whether to LOOK for a
        // culture; the value itself always comes from the session, so two hosts in one process taking
        // the union of this flag cannot make either read the other's culture.
        if (options.SupportedCultures.Count > 0)
        {
            RaskCulture.IsEnabled = true;
        }

        services.Add(new ServiceDescriptor(typeof(SessionCulture), typeof(SessionCulture), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IRaskCulture), static sp => sp.GetRequiredService<SessionCulture>(), lifetime));

        // Remembering nothing is only the fallback: the cookie is written through Rask.Web, which Core cannot
        // see, so a host that references it registers Rask.Web's CookieCulturePersistence first.
        services.TryAdd(new ServiceDescriptor(
            typeof(IRaskCulturePersistence), typeof(NullCulturePersistence), lifetime));

        return services;
    }
}
