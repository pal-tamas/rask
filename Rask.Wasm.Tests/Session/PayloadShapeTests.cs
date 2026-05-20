using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class PayloadShapeTests
{
    public PayloadShapeTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task InitialRender_AlwaysIncludesDataRaskRootEqualsWasm()
    {
        var (session, _) = NewSession();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("data-rask-root=\"wasm\"", html);
    }

    [Fact]
    public async Task InitialRender_NoCssRegistered_DoesNotIncludeCssText()
    {
        var (session, _) = NewSession();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }

    [Fact]
    public async Task InitialRender_FollowedByHandlerDispatch_DoesNotResendCssText_WhenHashUnchanged()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var payload =
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
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

    private static string ExtractFirstHandlerId(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }
}
