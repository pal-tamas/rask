using System.Reflection;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

#pragma warning disable RASK014 // tests render the demo components directly as roots

namespace Rask.Example.Shared.Tests.Pages;

// The error-boundary demos (folded into the Composition guide from their former standalone page): a
// handler-throw boundary, a render-throw boundary, and nested boundaries.
public sealed class BoomPageTests
{
    [Fact]
    public void Demos_AtRest_EmitTheirHostDivs()
    {
        var sp = TestServices.Default();

        Assert.Contains("boom-handler-host", new BoomHandlerDemo().RenderAsLiveRoot(sp));
        Assert.Contains("boom-render-host", new BoomRenderDemo().RenderAsLiveRoot(sp));
        Assert.Contains("boom-nested-host", new BoomNestedDemo().RenderAsLiveRoot(sp));
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
