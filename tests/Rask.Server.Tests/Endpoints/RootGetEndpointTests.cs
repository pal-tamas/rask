using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class RootGetEndpointTests
{
    [Fact]
    public async Task Get_ReturnsHtmlWithDataRaskRootAttribute()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/some-path");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root=\"", body);
    }

    [Fact]
    public async Task Get_RegistersSessionInStore()
    {
        using var host = RaskTestHost.Create<TestApp>();

        Assert.Equal(0, host.Store.Count);
        var response = await host.Http.GetAsync("/foo");
        response.EnsureSuccessStatusCode();

        Assert.Equal(1, host.Store.Count);
    }

    [Fact]
    public async Task Get_ShellResponse_IsNotCacheable()
    {
        // The shell embeds the session id (data-rask-root), the de-facto bearer for the WS /
        // upload / download endpoints, so it must never be cached by a shared proxy or bfcache.
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/some-path");

        response.EnsureSuccessStatusCode();
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.NoStore);
        Assert.True(cacheControl.NoCache);
        Assert.True(cacheControl.Private);
    }

    [Fact]
    public async Task Get_HonoursPathFromRequest()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/widgets/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("path=/widgets/42", body);
    }

    [Fact]
    public async Task Get_StoresQueryStringOnRouteState()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/q?x=1&y=two");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(body);

        var ls = host.Store.Get(sessionId);
        Assert.NotNull(ls);

        var routeState = ls!.Services.GetRequiredService<RouteState>();
        Assert.Equal("/q", routeState.Path);
        Assert.Equal("1", routeState.Query["x"].ToString());
        Assert.Equal("two", routeState.Query["y"].ToString());
    }
}
