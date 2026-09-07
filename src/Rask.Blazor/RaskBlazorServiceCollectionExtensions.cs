using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        // Through the options pattern rather than a hand-built instance (#948). `configure` used to run
        // on a fresh object that TryAddSingleton then DISCARDED whenever something had already
        // registered one — so a second AddRaskBlazor(o => …), which is what a library and an app each
        // calling it looks like, was silently ignored. Configure accumulates instead, and the standard
        // .NET seam is the one to reach for here rather than a bespoke registry.
        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<RaskBlazorOptions>>().Value);

        // Non-negotiable: most component libraries inject NavigationManager, and Blazor's base class
        // throws if it was never initialised — so without this, hosting MudBlazor or Radzen fails on
        // the first render with an exception naming none of this.
        services.TryAddScoped<NavigationManager>(
            sp =>
            {
                // NavigationManager.Initialize REQUIRES a base URI ending in '/', and throws an
                // ArgumentException naming neither Rask nor the option when it does not (#948). Someone
                // writing o.BaseUri = "https://example.com" has done nothing wrong, so normalise rather
                // than fail: the trailing slash is a detail of Blazor's contract, not a decision.
                var configured = sp.GetRequiredService<RaskBlazorOptions>().BaseUri;
                var baseUri = configured.EndsWith('/') ? configured : configured + "/";
                return new RaskNavigation(baseUri, baseUri);
            });

        // Throws with a message naming the fix, rather than no-opping. A silent no-op would turn a
        // real capability gap into a component that looks right and is subtly wrong.
        services.TryAddScoped<IJSRuntime, RaskBlazorJSRuntime>();

        return services;
    }
}
