using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// The coalescing loop renders a dispatch at most three times, then reports the rest as dropped. The server left the
// dropped request pending, so the dispatch's drain rendered it anyway — and every render it caused queued the next.
// WasmLiveSession consumes it on purpose; the server now does the same.
public class CoalescingBudgetTests
{
    [Fact]
    public async Task A_render_chain_longer_than_the_budget_stops_at_the_budget()
    {
        await using var fixture = await ConnectedSession.Connect<RerenderChainApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/next", query = "" }, ct: TestContext.Current.CancellationToken);
        await fixture.Ws.SettledAsync();

        var root = Assert.IsType<RootErrorBoundary>(fixture.Session.View);
        var app = Assert.IsType<RerenderChainApp>(root.Inner);
        Assert.Equal(3, app.RendersAfterNavigation);
    }
}
