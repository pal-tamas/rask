using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Regression: when a handler that calls Navigator.Navigate(...) also triggers
// a publish-render rebuild within the same dispatch (LiveTicker's
// OnRenderedAsync → Chart.js-draw continuation is the canonical case), the
// final payload must still carry the history.url. The prior
// BuildPayloadCoalescingRerendersAsync implementation dropped historyUrl on
// the rebuild — handler-initiated navigation silently lost its pushState and
// the browser URL stayed pinned to the previous path.
[Collection("WasmSession")]
public class NavigationPublishRerenderTests
{
    public NavigationPublishRerenderTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task NavigatorNavigate_WithPublishRenderRebuild_PayloadStillCarriesHistoryUrl()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<NavigateWithPublishRenderApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);

        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var result = await session.DispatchAsync(
            Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());

        // The handler called nav.Navigate("/destination") → the payload must
        // surface a `history.url` field even though the publish-render rebuild
        // ran. Pre-fix this assertion failed because the rebuild dropped
        // historyUrl on the floor.
        Assert.True(doc.RootElement.TryGetProperty("history", out var history),
            "Payload lost the history field after publish-render rebuild — handler navigation will not update the URL.");
        Assert.Equal("/destination", history.GetProperty("url").GetString());
        Assert.Equal("push", history.GetProperty("action").GetString());

        // Sanity: route state was actually updated by the handler.
        Assert.Equal("/destination", provider.GetRequiredService<RouteState>().Path);
    }

    private static string ExtractFirstHandlerId(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success, $"no handler id in payload html: {html}");
        return match.Groups[1].Value;
    }
}
