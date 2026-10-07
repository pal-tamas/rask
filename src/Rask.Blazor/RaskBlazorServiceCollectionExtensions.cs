using Microsoft.Extensions.DependencyInjection;

namespace Rask.Blazor;

/// <summary>Configures how Blazor components are hosted.</summary>
public static class RaskBlazorServiceCollectionExtensions
{
    /// <summary>
    ///     Changes the options a hosted Blazor component is rendered with.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Optional. Referencing the package is the whole setup: an island supplies the
    ///         <c>NavigationManager</c> and <c>IJSRuntime</c> a component library expects whenever the
    ///         app has not registered its own. Call this only to change a
    ///         <see cref="RaskBlazorOptions" /> value — a library's stylesheet, most often.
    ///     </para>
    ///     <para>
    ///         Calls accumulate, so a component library and the app can each make one.
    ///     </para>
    /// </remarks>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">The options to change — see <see cref="RaskBlazorOptions" />.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskBlazor(
        this IServiceCollection services,
        Action<RaskBlazorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The options pattern rather than a hand-built instance (#948): Configure accumulates, where a
        // TryAddSingleton of a configured object silently discarded every call after the first.
        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }
}
