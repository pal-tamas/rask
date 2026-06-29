using Rask.Core.Browser;
using BrowserPerformance = Rask.Core.Browser.Performance;

namespace Rask.Core.Tests.Interop;

public class PerformanceTests
{
    [Fact]
    public async Task Now_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPerf.now", 1234.5);

        Assert.Equal(1234.5, await new BrowserPerformance(js).NowAsync());
    }

    [Fact]
    public async Task GetNavigationTiming_ReturnsSnapshot_FromHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPerf.navigation", new NavigationTiming(40, 120, 130, 250, 260));

        var t = await new BrowserPerformance(js).GetNavigationTimingAsync();

        Assert.NotNull(t);
        Assert.Equal(40, t!.TimeToFirstByteMs);
        Assert.Equal(130, t.DomContentLoadedMs);
        Assert.Equal(250, t.LoadMs);
        Assert.Equal(260, t.DurationMs);
    }

    [Fact]
    public async Task GetNavigationTiming_ReturnsNull_WhenNoEntry()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new BrowserPerformance(js).GetNavigationTimingAsync());
    }
}
