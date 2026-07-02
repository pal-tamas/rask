using System.Reflection;
using System.Text;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class DownloadPageTests
{
    [Fact]
    public void Render_EmitsDownloadButton_AndZeroCount()
    {
        // Render DownloadDemo directly — its standalone /download page was folded into
        // docs/http-and-files.md, where the demo is embedded as a live sample.
        var nav = new Navigator(new RouteState { Path = "/" }, new CapturingDownloadSink());
        var html = new DownloadDemo(nav).RenderAsLiveRoot(TestServices.Default());

        Assert.Contains("download-report", html);
        Assert.Contains("Generated 0 time(s)", html);
    }

    [Fact]
    public void DownloadReport_FromHandler_StagesBytesThroughDownloadSink()
    {
        var sink = new CapturingDownloadSink();
        var routeState = new RouteState { Path = "/download" };
        var nav = new Navigator(routeState, sink);
        var page = new DownloadDemo(nav);

        var mi = typeof(DownloadDemo).GetMethod("DownloadReport",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        TestNavigator.RunHandler(nav, () => mi.Invoke(page, null));

        Assert.Single(sink.Captured);
        var captured = sink.Captured[0];
        Assert.Equal("report.txt", captured.Filename);
        Assert.Equal("text/plain", captured.ContentType);
        var text = Encoding.UTF8.GetString(captured.Bytes);
        Assert.Contains("Rask download demo", text);
        Assert.Contains("Count: 1", text);
    }

    [Fact]
    public void DownloadReport_MultipleClicks_IncrementCount()
    {
        var sink = new CapturingDownloadSink();
        var routeState = new RouteState { Path = "/download" };
        var nav = new Navigator(routeState, sink);
        var page = new DownloadDemo(nav);

        var mi = typeof(DownloadDemo).GetMethod("DownloadReport",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        TestNavigator.RunHandler(nav, () => mi.Invoke(page, null));
        TestNavigator.RunHandler(nav, () => mi.Invoke(page, null));
        TestNavigator.RunHandler(nav, () => mi.Invoke(page, null));

        Assert.Equal(3, sink.Captured.Count);
        Assert.Contains("Count: 3", Encoding.UTF8.GetString(sink.Captured[^1].Bytes));
    }
}
