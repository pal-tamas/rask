using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class HomePageTests
{
    [Fact]
    public void Render_AtRoot_EmitsHeroAndAllFeatureCards()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("The Rask framework", html);
        Assert.Contains("hero-card", html);
        Assert.Contains("Start with Tags", html);
        Assert.Contains("Source on GitHub", html);
        // Feature cards
        Assert.Contains(">DSL<", html);
        Assert.Contains(">Components<", html);
        Assert.Contains(">Routing<", html);
        Assert.Contains(">Scoped CSS<", html);
        // "+" is HTML-encoded inside element text content as &#x2B;.
        Assert.Contains("HttpClient &#x2B; DI", html);
    }

    [Fact]
    public void Render_EmitsHelloWorldSnippet()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("Hello, world!", html);
    }
}
