using System.Reflection;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class BoomPageTests
{
    [Fact]
    public void Render_AtRest_EmitsAllThreeHostDivs()
    {
        var html = new Shared.App().RenderAsLiveRoot(
            TestServices.Default(routeState: new RouteState { Path = "/boom" }));

        Assert.Contains("boom-handler-host", html);
        Assert.Contains("boom-render-host", html);
        Assert.Contains("boom-nested-host", html);
    }

    [Fact]
    public void ThrowFromHandler_ThrowsInvalidOperation_WithBoundaryDemoMessage()
    {
        var mi = typeof(BoomHandlerDemo).GetMethod("ThrowFromHandler",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var ex = Assert.Throws<TargetInvocationException>(() => mi.Invoke(null, null));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("kaboom", ex.InnerException!.Message);
    }

    [Fact]
    public void ThrowFromInnerHandler_ThrowsInvalidOperation_WithInnerBoundaryDemoMessage()
    {
        var mi = typeof(BoomNestedDemo).GetMethod("ThrowFromInnerHandler",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var ex = Assert.Throws<TargetInvocationException>(() => mi.Invoke(null, null));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("inner boundary demo", ex.InnerException!.Message);
    }

    [Fact]
    public void RenderThrower_NestedType_RendersByThrowing()
    {
        // BoomRenderDemo's private RenderThrower deliberately throws from Render(); ensure
        // the type still exists with that contract so the ErrorBoundary demo keeps
        // demonstrating the render-throw path.
        var nested = typeof(BoomRenderDemo).GetNestedType("RenderThrower",
            BindingFlags.NonPublic);
        Assert.NotNull(nested);
        Assert.True(typeof(Component).IsAssignableFrom(nested!));
        Assert.True(nested.GetCustomAttribute<SkipFactoryAttribute>() is not null);
    }
}
