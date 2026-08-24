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
        Assert.Contains("derive it from", ex.Message);
    }

    [Fact]
    public void GetLocalTemplate_PageWithRouteOverride_ResolvesFromTheRegistry()
    {
        // A Page carries no [Route] to reflect over — its template is read at compile time into the
        // registry. This is what keeps the no-template Route.To<T>() overload working for a Page.
        Assert.Equal(
            "/__resolver-test/page",
            RouteTemplateResolver.GetLocalTemplate(typeof(RouteTemplateResolverPage)));
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
public sealed partial class RouteTemplateResolverAnnotatedPage : Component
{
    protected override Component? Render() => Span;
}

public sealed partial class RouteTemplateResolverUnannotatedPage : Component
{
    protected override Component? Render() => Span;
}

[Route("/__resolver-test/page")]
public sealed partial class RouteTemplateResolverPage : Component
{
    protected override Component? Render() => Span;
}
