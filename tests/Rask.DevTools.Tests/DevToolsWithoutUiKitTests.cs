using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Endpoints;
using Rask.Server.DevTools;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests;

/// <summary>
///     The panel is drawn with the app's own Rask.Ui, which the devtools package does not bring. An app without it gets
///     no devtools at all — no tag, no host script, no panel — rather than a pill that opens a broken frame.
/// </summary>
public sealed class DevToolsWithoutUiKitTests
{
    [Fact]
    public async Task Without_the_kit_a_development_page_loads_no_devtools()
    {
        using var host = RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s =>
            {
                // Registered before the attach, which only TryAdds: this stands in for an app without Rask.Ui.
                s.AddSingleton<IRaskServerDevTools>(new DevToolsServerEndpoints(() => false));
                RaskDevToolsLoader.Attach(s);
            },
            environment: "Development");

        var html = await host.Http.GetStringAsync("/");

        // The page itself must still be live, or the absence below would prove nothing.
        Assert.Contains("data-rask-root=", html, StringComparison.Ordinal);
        Assert.DoesNotContain(DevToolsServerEndpoints.Prefix, html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            e => e.RoutePattern.RawText == DevToolsServerEndpoints.HostScriptPath);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.Http.GetAsync(DevToolsServerEndpoints.Prefix + "/?inspect=x&t=y")).StatusCode);
    }

    [Fact]
    public void The_kit_probe_names_the_kit_the_panel_is_drawn_with()
    {
        var kit = typeof(Rask.Ui.UiStylesheet);

        Assert.Equal(DevToolsUiKit.ProbeTypeName, kit.FullName + ", " + kit.Assembly.GetName().Name);
        Assert.True(DevToolsUiKit.IsAvailable());
    }
}
