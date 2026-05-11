using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteTemplateResolverTests
{
    [Fact]
    public void GetLocalTemplate_AnnotatedType_ReturnsTemplate()
    {
        Assert.Equal(
            "/__resolver-test/annotated",
            RouteTemplateResolver.GetLocalTemplate(typeof(RouteTemplateResolverAnnotatedPage)));
    }

    [Fact]
    public void GetLocalTemplate_UnannotatedType_ThrowsInvalidOperationWithFullName()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RouteTemplateResolver.GetLocalTemplate(typeof(RouteTemplateResolverUnannotatedPage)));

        Assert.Contains(typeof(RouteTemplateResolverUnannotatedPage).FullName!, ex.Message);
        Assert.Contains("[Route(\"...\")]", ex.Message);
    }

    [Fact]
    public void GetLocalTemplate_Cached_ReturnsSameStringInstance()
    {
        var first = RouteTemplateResolver.GetLocalTemplate(typeof(RouteTemplateResolverAnnotatedPage));
        var second = RouteTemplateResolver.GetLocalTemplate(typeof(RouteTemplateResolverAnnotatedPage));

        Assert.Same(first, second);
    }
}

[Route("/__resolver-test/annotated")]
public sealed class RouteTemplateResolverAnnotatedPage : Component
{
    public override Component Render() => new Span(null);
}

public sealed class RouteTemplateResolverUnannotatedPage : Component
{
    public override Component Render() => new Span(null);
}
