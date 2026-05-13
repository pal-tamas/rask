using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class DefaultNotFoundPageTests
{
    [Fact]
    public void Render_IncludesPageNotFoundHeading()
    {
        using var _ = BeginRoute("/missing");

        var html = new DefaultNotFoundPage().RenderForLive().ToHtml();

        Assert.Contains("Page not found", html);
    }

    [Fact]
    public void Render_IncludesRequestedPath()
    {
        using var _ = BeginRoute("/does/not/exist");

        var html = new DefaultNotFoundPage().RenderForLive().ToHtml();

        Assert.Contains("/does/not/exist", html);
    }

    [Fact]
    public void Render_LinksBackToHome()
    {
        using var _ = BeginRoute("/anywhere");

        var html = new DefaultNotFoundPage().RenderForLive().ToHtml();

        Assert.Contains("href=\"/\"", html);
    }

    [Fact]
    public void Render_NoRouteStateRegistered_FallsBackToRoot()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        using var _ = LiveRenderContext.Begin(new StubComponent(new Span()), services);

        var html = new DefaultNotFoundPage().RenderForLive().ToHtml();

        Assert.Contains("Page not found", html);
        Assert.Contains(">/<", html);
    }

    private static IDisposable BeginRoute(string path)
    {
        var state = new RouteState { Path = path };
        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(new Span()), services);
    }
}
