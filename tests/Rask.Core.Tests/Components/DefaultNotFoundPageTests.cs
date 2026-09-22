using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class DefaultNotFoundPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_page_shows_a_page_not_found_heading()
    {
        using var _ = BeginRoute("/missing");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("Page not found", html);
    }

    [Fact]
    public void The_page_shows_the_requested_path()
    {
        using var _ = BeginRoute("/does/not/exist");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("/does/not/exist", html);
    }

    [Fact]
    public void The_page_links_back_to_home()
    {
        using var _ = BeginRoute("/anywhere");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("href=\"/\"", html);
    }

    [Fact]
    public void With_no_route_state_registered_the_path_falls_back_to_the_root()
    {
        var services = RenderHarness.EmptyServices();
        using var _ = LiveRenderContext.Begin(new StubComponent(Span), services);

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("Page not found", html);
        Assert.Contains(">/<", html);
    }

    private static IDisposable BeginRoute(string path)
    {
        var state = new RouteState { Path = path };
        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(Span), services);
    }
}
