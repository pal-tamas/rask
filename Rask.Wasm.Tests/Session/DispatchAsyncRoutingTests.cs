using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class DispatchAsyncRoutingTests
{
    public DispatchAsyncRoutingTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task Dispatch_EmptyJson_ReturnsEmptyBytes()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync(Array.Empty<byte>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatch_TypeNavigate_RoutesToNavigateBranch_AndUpdatesRouteState()
    {
        var (session, services) = NewSession();
        var routeState = services.GetRequiredService<RouteState>();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("/destination", routeState.Path);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Dispatch_NavigateEmptyPath_ReturnsEmpty()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":""}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatch_NavigateReplaceTrue_EmitsHistoryReplace()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/x","replace":true}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Dispatch_NoHandlerIdAndNoType_ReturnsEmpty()
    {
        var (session, _) = NewSession();

        var result = await session.DispatchAsync(Utf8("""{"foo":"bar"}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatch_UnknownHandlerId_ReturnsEmpty()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"id":"h999"}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatch_KnownHandler_ReturnsPayloadWithUpdatedHtml()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();

        var handlerId = ExtractFirstHandlerId(initial);
        var result = await session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}","type":"click"}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("count=1", html);
    }

    [Fact]
    public async Task Dispatch_NavigateWithEmptyQuery_NoQuestionMarkInHistoryUrl()
    {
        var (session, _) = NewSession();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/x","query":""}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("/x", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Dispatch_ConcurrentCalls_SerialisedByLock()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}","type":"click"}""")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var counts = results.Select(r =>
        {
            using var doc = JsonDocument.Parse(r.AsMemory());
            var html = doc.RootElement.GetProperty("html").GetString()!;
            return int.Parse(Regex.Match(html, "count=(\\d+)").Groups[1].Value);
        }).OrderBy(c => c).ToArray();

        // Lock guarantees 5 distinct sequential counts 1..5.
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, counts);
    }

    [Fact]
    public async Task Dispatch_HandlerThatThrows_ReturnsEmpty_AndSessionStaysUsable()
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

        var result = await session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}"}"""));

        Assert.Empty(result);

        // Session is still usable: a subsequent valid dispatch (using an unknown handler id) still returns empty,
        // proving the lock was released and the session didn't crash.
        var follow = await session.DispatchAsync(Utf8("""{"id":"h999"}"""));
        Assert.Empty(follow);
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

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static string ExtractFirstHandlerId(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
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
