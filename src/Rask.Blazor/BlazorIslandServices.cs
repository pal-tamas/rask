using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Rask.Core.Routing;

namespace Rask.Blazor;

/// <summary>
///     The application's services, plus the three a hosted Blazor component expects and an app has no
///     reason to register.
/// </summary>
/// <remarks>
///     This is what makes referencing the package the whole setup. A hosted component resolves from
///     the app's own container — it should see the same DI as the rest of the app — and only when the
///     app has no answer for <see cref="NavigationManager" />, <see cref="IJSRuntime" /> or
///     <see cref="RaskBlazorOptions" /> does the island supply one. Anything the app registers wins.
/// </remarks>
internal sealed class BlazorIslandServices(IServiceProvider app) : IKeyedServiceProvider, IDisposable
{
    private RaskNavigation? _navigation;
    private RaskBlazorJSRuntime? _js;

    /// <summary>Runs a callback on the hosted component's dispatcher. Set once the renderer exists.</summary>
    public Func<Action, Task> OnRenderer { get; set; } = static _ => Task.CompletedTask;

    /// <summary>The options the app configured through <c>AddRaskBlazor</c>, or the defaults.</summary>
    public static RaskBlazorOptions OptionsOf(IServiceProvider app) =>
        app.GetService<IOptions<RaskBlazorOptions>>()?.Value ?? Defaults;

    private static RaskBlazorOptions Defaults { get; } = new();

    /// <summary>What a render with no application services resolves from: nothing but the island's own three.</summary>
    public static IServiceProvider None { get; } = new ServiceCollection().BuildServiceProvider();

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        if (app.GetService(serviceType) is { } registered)
        {
            return registered;
        }

        if (serviceType == typeof(NavigationManager))
        {
            return _navigation ??= new RaskNavigation(
                OptionsOf(app).BaseUri,
                app.GetService<RouteState>(),
                app.GetService<Navigator>(),
                callback => OnRenderer(callback));
        }

        if (serviceType == typeof(IJSRuntime))
        {
            return _js ??= new RaskBlazorJSRuntime();
        }

        return serviceType == typeof(RaskBlazorOptions) ? OptionsOf(app) : null;
    }

    /// <inheritdoc />
    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        (app as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

    /// <inheritdoc />
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        app.GetRequiredKeyedService(serviceType, serviceKey);

    /// <inheritdoc />
    public void Dispose() => _navigation?.Dispose();
}
