using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     Referencing the package is the whole setup: a hosted component gets a working
///     <see cref="NavigationManager" /> with nothing registered, and it is Rask's own routing.
/// </summary>
[Collection(nameof(PathBaseCollection))]
public partial class NavigationTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services(RouteState route, Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection().AddSingleton(route);
        more?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_hosted_component_resolves_NavigationManager_with_nothing_registered()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var html = Page.Render(LocationIsland.Label("here"), services).Html;

        Assert.Contains("uri: http://localhost/", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosted_NavigationManager_reports_the_current_route_and_path_base()
    {
        var previous = LiveOptions.PathBase;
        LiveOptions.PathBase = "/shop";

        try
        {
            var html = Page.Render(LocationIsland.Label("here"), Services(TestRoute.At("/orders?page=2"))).Html;

            Assert.Contains("base: http://localhost/shop/", html, StringComparison.Ordinal);
            Assert.Contains("uri: http://localhost/shop/orders?page=2", html, StringComparison.Ordinal);
        }
        finally
        {
            LiveOptions.PathBase = previous;
        }
    }

    [Fact]
    public async Task NavigateTo_from_a_hosted_handler_moves_the_Rask_route()
    {
        var route = TestRoute.At("/");
        var page = Page.Render(LocationIsland.Label("go"), Services(route));

        await page.On("[data-rask-on-click]").Click();

        Assert.Equal("/orders", route.Path);
        Assert.Equal("2", route.Query["page"].ToString());
    }

    [Fact]
    public async Task A_Rask_navigation_raises_LocationChanged_in_the_hosted_component()
    {
        var route = TestRoute.At("/");
        var page = Page.Render(LocationIsland.Label("go"), Services(route));

        await page.On("[data-rask-on-click]").Click();

        Assert.Contains("uri: http://localhost/orders?page=2", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("changes: 0", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_registered_NavigationManager_wins_over_the_islands_own()
    {
        var services = Services(
            TestRoute.At("/"),
            s => s.AddSingleton<NavigationManager>(new FixedNavigation("https://example.test/app/")));

        var html = Page.Render(LocationIsland.Label("here"), services).Html;

        Assert.Contains("uri: https://example.test/app/", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_configured_through_AddRaskBlazor_still_reach_the_island()
    {
        var services = Services(TestRoute.At("/orders"), s => s.AddRaskBlazor(o => o.BaseUri = "https://shop.test"));

        var html = Page.Render(LocationIsland.Label("here"), services).Html;

        Assert.Contains("uri: https://shop.test/orders", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRaskBlazor_registers_nothing_an_app_might_want_to_own()
    {
        var services = new ServiceCollection();

        services.AddRaskBlazor();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(NavigationManager));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(Microsoft.JSInterop.IJSRuntime));
    }
}

/// <summary>Prints where its NavigationManager says it is, and navigates on click.</summary>
public sealed class LocationBox : ComponentBase, IDisposable
{
    private int _changes;

    [Parameter] public string? Label { get; set; }

    [Inject] public NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        _changes++;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, () => Navigation.NavigateTo("orders?page=2")));
        builder.AddContent(2, Label);
        builder.CloseElement();
        builder.OpenElement(3, "p");
        builder.AddContent(4, $"base: {Navigation.BaseUri} uri: {Navigation.Uri} changes: {_changes}");
        builder.CloseElement();
    }
}

public sealed partial class LocationIsland : BlazorComponent<LocationBox>;

/// <summary>LiveOptions.PathBase is process-wide, so the class that sets it runs alone.</summary>
[CollectionDefinition(nameof(PathBaseCollection), DisableParallelization = true)]
public sealed class PathBaseCollection;

internal sealed class FixedNavigation : NavigationManager
{
    public FixedNavigation(string uri) => Initialize(uri, uri);
}
