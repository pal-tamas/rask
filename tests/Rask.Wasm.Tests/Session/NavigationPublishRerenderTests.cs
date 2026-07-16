using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Regression: when a handler that calls Navigator.NavigateTo(...) also triggers
// a publish-render rebuild within the same dispatch (LiveTicker's
// OnRenderedAsync → Chart.js-draw continuation is the canonical case), the
// final payload must still carry the history.url. The prior
// BuildPayloadCoalescingRerendersAsync implementation dropped historyUrl on
// the rebuild — handler-initiated navigation silently lost its pushState and
// the browser URL stayed pinned to the previous path.
[Collection("WasmSession")]
public class NavigationPublishRerenderTests : ResettingTestBase
{
    [Fact]
    public async Task NavigatorNavigate_WithPublishRenderRebuild_PayloadStillCarriesHistoryUrl()
    {
        var (session, provider) = NewSession<NavigateWithPublishRenderApp>();

        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        var result = await session.DispatchAsync(
            Utf8($$"""{"id":"{{handlerId}}","type":"click"}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());

        // The handler called nav.NavigateTo("/destination") → the payload must
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
}
