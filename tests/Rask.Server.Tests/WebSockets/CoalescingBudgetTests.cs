using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// The coalescing loop renders a dispatch at most three times. A request still pending then is not dropped — that
// left the state last written undrawn until the next event — and becomes one render after the dispatch. That one is
// a publish render, which fires no OnRendered on a component that has rendered before, so a chain that asks from
// every OnRendered ends there.
public class CoalescingBudgetTests
{
    [Fact]
    public async Task A_render_chain_longer_than_the_budget_is_the_budget_and_one_render_after_the_dispatch()
    {
        await using var fixture = await ConnectedSession.Connect<RerenderChainApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/next", query = "" }, ct: TestContext.Current.CancellationToken);
        await fixture.Ws.SettledAsync();

        var root = Assert.IsType<RootErrorBoundary>(fixture.Session.View);
        var app = Assert.IsType<RerenderChainApp>(root.Inner);
        Assert.Equal(4, app.RendersAfterNavigation);
    }

    // Each render of the chain differs from the last, so each is a frame. The address is the navigation's, said
    // once: a second frame that repeated it would push the same history entry again, and Back would stay put.
    [Fact]
    public async Task A_navigation_that_renders_more_than_once_gives_the_browser_its_address_once()
    {
        await using var fixture = await ConnectedSession.Connect<RerenderChainApp>();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/next", query = "" }, ct: TestContext.Current.CancellationToken);
        var frames = await fixture.Ws.SettledAsync();

        Assert.True(frames.Count > 1, $"the chain was expected to send several frames, and sent {frames.Count}");
        Assert.Equal("/next", LiveFrames.HistoryUrl(frames[0]));
        Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
    }
}
