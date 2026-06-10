using System.Reflection;
using Rask.Core.Routing;
using Rask.Example.Shared.Layout;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class NotFoundPageTests
{
    [Fact]
    public void Render_ShowsRouteInBody()
    {
        var routeState = new RouteState { Path = "/__unknown" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("Page not found", html);
        Assert.Contains("/__unknown", html);
        Assert.Contains(">Back to welcome<", html);
    }

    [Fact]
    public void NotFoundAttribute_AppliedToType() =>
        Assert.NotNull(typeof(NotFoundPage).GetCustomAttribute<NotFoundAttribute>());

    [Fact]
    public void HasParentRoute_ShowcaseLayout()
    {
        var parent = typeof(NotFoundPage).GetCustomAttribute<ParentRouteAttribute>();
        Assert.NotNull(parent);
        Assert.Equal(typeof(ShowcaseLayout), parent!.Parent);
    }
}
