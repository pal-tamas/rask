using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Routing;
using Rask.Example.Server.Tests.Infrastructure;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;

namespace Rask.Example.Server.Tests.Hosting;

public sealed class ProgramTests
{
    [Fact]
    public void AddRaskAndSingletons_RegisterExpectedServices()
    {
        using var host = ExampleServerTestHost.Create();
        // Resolve scope-aware services (RouteState, Navigator, IJSRuntime) from a
        // request scope; resolve singletons (HttpClient, IBannedWordService) from root.
        using var scope = host.Server.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetService<RouteState>());
        Assert.NotNull(sp.GetService<Navigator>());
        Assert.NotNull(sp.GetService<IJSRuntime>());

        // The HTTP demo now points HttpClient at the server's own origin (it fetches
        // a static data/posts-1.json it serves itself), not an external API. Over the
        // in-memory TestServer that has no bound address, so the resolver falls back
        // to localhost.
        var http = host.Server.Services.GetService<HttpClient>();
        Assert.NotNull(http);
        Assert.Equal(new Uri("http://localhost/"), http!.BaseAddress);

        var banned = host.Server.Services.GetService<IBannedWordService>();
        Assert.NotNull(banned);
        Assert.IsType<BannedWordService>(banned);
    }

    [Fact]
    public async Task RootGet_Returns200_AndDoctypeHtml()
    {
        using var host = ExampleServerTestHost.Create();
        var response = await host.Http.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE html>", body);
        Assert.Contains("<html lang=\"en\">", body);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsHtmlWithNotFoundPageMarker()
    {
        using var host = ExampleServerTestHost.Create();
        var response = await host.Http.GetAsync("/__no_such_path");
        var body = await response.Content.ReadAsStringAsync();
        // The framework's catch-all routes to the NotFound page; it returns 200 with HTML.
        Assert.Contains("Page not found", body);
    }
}
