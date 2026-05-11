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

    [Fact]
    public async Task Dispatch_NavigateWithEmptyQuery_NoQuestionMarkInHistoryUrl()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync("""{"type":"navigate","path":"/x","query":""}""");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("/x", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Dispatch_ConcurrentCalls_SerialisedByLock()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => session.DispatchAsync($$"""{"id":"{{handlerId}}","type":"click"}"""))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var counts = results.Select(r =>
        {
            using var doc = JsonDocument.Parse(r);
            var html = doc.RootElement.GetProperty("html").GetString()!;
            return int.Parse(Regex.Match(html, "count=(\\d+)").Groups[1].Value);
        }).OrderBy(c => c).ToArray();

        // Lock guarantees 5 distinct sequential counts 1..5.
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, counts);
    }

    [Fact]
    public async Task Dispatch_HandlerThatThrows_ReturnsEmptyString_AndSessionStaysUsable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<ThrowingStubApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);

        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var result = await session.DispatchAsync($$"""{"id":"{{handlerId}}"}""");

        Assert.Equal(string.Empty, result);

        // Session is still usable: a subsequent valid dispatch (using an unknown handler id) still returns empty,
        // proving the lock was released and the session didn't crash.
        var follow = await session.DispatchAsync("""{"id":"h999"}""");
        Assert.Equal(string.Empty, follow);
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
