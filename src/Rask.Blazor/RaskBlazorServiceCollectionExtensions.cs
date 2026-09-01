using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Rask.Blazor;

/// <summary>Registers what a hosted Blazor component expects to resolve.</summary>
public static class RaskBlazorServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the services a hosted Blazor component needs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Call this once in <c>Program.cs</c> when the app hosts any
    ///         <c>BlazorComponent&lt;T&gt;</c>. A component library resolves its dependencies from the
    ///         application's own container, which is right — a hosted component should see the same
    ///         DI as the rest of the app — but two of them have no application-side answer and are
    ///         supplied here.
    ///     </para>
    ///     <para>
    ///         <c>TryAdd</c> throughout, so an app that already registers a real
    ///         <see cref="NavigationManager" /> or <see cref="IJSRuntime" /> keeps its own.
    ///     </para>
    /// </remarks>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">Optional knobs — see <see cref="RaskBlazorOptions" />.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskBlazor(
        this IServiceCollection services,
        Action<RaskBlazorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskBlazorOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        // Non-negotiable: most component libraries inject NavigationManager, and Blazor's base class
        // throws if it was never initialised — so without this, hosting MudBlazor or Radzen fails on
        // the first render with an exception naming none of this.
        services.TryAddScoped<NavigationManager>(
            sp => new RaskNavigation(
                sp.GetRequiredService<RaskBlazorOptions>().BaseUri,
                sp.GetRequiredService<RaskBlazorOptions>().BaseUri));

        // Throws with a message naming the fix, rather than no-opping. A silent no-op would turn a
        // real capability gap into a component that looks right and is subtly wrong.
        services.TryAddScoped<IJSRuntime, RaskBlazorJSRuntime>();

        return services;
    }
}
