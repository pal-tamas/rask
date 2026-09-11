using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     UseRask maps the devtools' endpoints only when the devtools attached. This assembly does not reference
///     Rask.DevTools, so even a Debug build — switch on, environment Development — finds no bootstrap and must
///     map nothing under <c>/_rask-devtools</c>.
/// </summary>
public class DevToolsEndpointAbsenceTests
{
    [Fact]
    public void A_host_without_the_devtools_maps_no_devtools_endpoint()
    {
        using var host = RaskTestHost.Create<TestApp>(environment: "Development");

        var patterns = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty);

        Assert.DoesNotContain(patterns, p => p.Contains("_rask-devtools", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_from_a_host_without_the_devtools_loads_no_host_script()
    {
        using var host = RaskTestHost.Create<TestApp>(environment: "Development");

        var html = await host.Http.GetStringAsync("/");

        // The page itself must still be live, or the absence below would prove nothing.
        Assert.Contains("data-rask-root=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("_rask-devtools", html, StringComparison.Ordinal);
    }
}
