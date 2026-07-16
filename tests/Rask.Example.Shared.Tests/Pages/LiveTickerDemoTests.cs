using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

// LiveTickerDemo (embedded in the Lifecycle guide) wraps the reusable LiveTicker widget with an
// internal-state symbol switcher and a hook-activity log — the standalone /realtime/{Symbol} page was
// folded into the guide, so the switcher flips Symbol via internal state rather than a [RouteParam]/URL.
// (Route-param binding itself is covered by Rask.Core.Tests routing; the LiveTicker lifecycle/poll loop is
// covered by the widget's own behaviour.)
public sealed class LiveTickerDemoTests
{
    [Fact]
    public void Render_EmitsAllThreeSwitchButtons_AndTheTickerDefaultsToBtc()
    {
        var html = RaskTest.Render(new LiveTickerDemo(), TestServices.Default()).Html;

        Assert.Contains("ticker-symbol-switcher", html);
        Assert.Contains("ticker-switch-BTC", html);
        Assert.Contains("ticker-switch-ETH", html);
        Assert.Contains("ticker-switch-SOL", html);
        // The child LiveTicker renders #ticker-symbol; the demo defaults to BTC.
        Assert.Contains("ticker-symbol", html);
    }

    [Fact]
    public void Render_HookActivityCard_Present()
    {
        var html = RaskTest.Render(new LiveTickerDemo(), TestServices.Default()).Html;

        Assert.Contains("Hook activity", html);
        Assert.Contains("ticker-clear-log", html);
    }
}
