using System.Reflection;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

#pragma warning disable RASK014 // tests render the demo components directly as roots

namespace Rask.Site.Tests.Pages;

// The error-boundary demos (folded into the Composition guide from their former standalone page): a
// handler-throw boundary, a render-throw boundary, and nested boundaries.
public sealed partial class BoomPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_demos_at_rest_emit_their_host_divs()
    {
        var sp = TestServices.Default();

        Assert.Contains("boom-handler-host", Page.Render(new BoomHandlerDemo(), sp).Html);
        Assert.Contains("boom-render-host", Page.Render(new BoomRenderDemo(), sp).Html);
        Assert.Contains("boom-nested-host", Page.Render(new BoomNestedDemo(), sp).Html);
    }

    [Fact]
    public async Task A_tripped_boundary_says_a_fixed_sentence_and_nothing_of_the_exception()
    {
        var page = Page.Render(() => BoomHandlerDemo, TestServices.Default());

        await page.On("#boom-throw").Click();

        Assert.Contains("That did not work.", page.Html, StringComparison.Ordinal);
        Assert.Contains("boom-recover", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("kaboom", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_render_that_throws_is_answered_with_a_fixed_sentence_and_nothing_of_the_exception()
    {
        var page = Page.Render(() => BoomRenderDemo, TestServices.Default());

        await page.On("#boom-render-trigger").Click();

        Assert.Contains("This part could not be shown.", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("kaboom", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_inner_boundary_says_a_fixed_sentence_and_leaves_the_outer_region_standing()
    {
        var page = Page.Render(() => BoomNestedDemo, TestServices.Default());

        await page.On("#boom-nested-throw").Click();

        Assert.Contains("The inner part stopped.", page.Html, StringComparison.Ordinal);
        Assert.Contains("boom-nested-outer-healthy", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("kaboom", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_handler_throws_an_InvalidOperationException_with_the_boundary_demo_message()
    {
        var mi = typeof(BoomHandlerDemo).GetMethod("ThrowFromHandler",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var ex = Assert.Throws<TargetInvocationException>(() => mi.Invoke(null, null));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("kaboom", ex.InnerException!.Message);
    }

    [Fact]
    public void The_inner_handler_throws_an_InvalidOperationException_with_the_inner_boundary_demo_message()
    {
        var mi = typeof(BoomNestedDemo).GetMethod("ThrowFromInnerHandler",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var ex = Assert.Throws<TargetInvocationException>(() => mi.Invoke(null, null));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("inner boundary demo", ex.InnerException!.Message);
    }

    [Fact]
    public void The_nested_RenderThrower_type_renders_by_throwing()
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
