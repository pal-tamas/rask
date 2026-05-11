using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class DispatchAsyncRoutingTests
{
    public DispatchAsyncRoutingTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task Dispatch_EmptyJson_ReturnsEmptyString()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Dispatch_TypeNavigate_RoutesToNavigateBranch_AndUpdatesRouteState()
    {
        var (session, services) = NewSession();
        var routeState = services.GetRequiredService<RouteState>();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync("""{"type":"navigate","path":"/destination","query":""}""");

        Assert.NotEqual(string.Empty, result);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("/destination", routeState.Path);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Dispatch_NavigateEmptyPath_ReturnsEmpty()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync("""{"type":"navigate","path":""}""");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Dispatch_NavigateReplaceTrue_EmitsHistoryReplace()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync("""{"type":"navigate","path":"/x","replace":true}""");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Dispatch_NoHandlerIdAndNoType_ReturnsEmpty()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync("""{"foo":"bar"}""");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Dispatch_UnknownHandlerId_ReturnsEmpty()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync("""{"id":"h999"}""");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Dispatch_KnownHandler_ReturnsPayloadWithUpdatedHtml()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();

        var handlerId = ExtractFirstHandlerId(initial);
        var result = await session.DispatchAsync($$"""{"id":"{{handlerId}}","type":"click"}""");

        Assert.NotEqual(string.Empty, result);
        using var doc = JsonDocument.Parse(result);
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("count=1", html);
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<StubApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);
        return (session, provider);
    }

    private static string ExtractFirstHandlerId(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var html = doc.RootElement.GetProperty("html").GetString()!;
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success, $"no handler id in payload html: {html}");
        return match.Groups[1].Value;
    }
}

[CollectionDefinition("WasmSession", DisableParallelization = true)]
public class WasmSessionCollection
{
}
