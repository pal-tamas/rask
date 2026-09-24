using System.Reflection;
using System.Text;
using Rask.Core.Routing;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

public sealed class DownloadPageTests
{
    [Fact]
    public void Rendering_emits_the_download_button_and_a_zero_count()
    {
        // Render DownloadDemo directly — its standalone /download page was folded into
        // docs/http-and-files.md, where the demo is embedded as a live sample.
        var nav = new Navigator(new RouteState { Path = "/" }, new CapturingDownloadSink());

        var html = Page.Render(new DownloadDemo(nav), TestServices.Default()).Html;

        Assert.Contains("download-report", html);
        Assert.Contains("Generated 0 time(s)", html);
    }

    [Fact]
    public void Downloading_a_report_from_a_handler_stages_the_bytes_through_the_download_sink()
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
    public void Multiple_download_clicks_increment_the_count()
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
