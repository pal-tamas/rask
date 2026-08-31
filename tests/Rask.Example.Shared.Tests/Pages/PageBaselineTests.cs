using System.Reflection;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

// Baseline smoke for every routed page: it renders via App at its registered path,
// without throwing, and contributes at least one <title> tag through the head pipeline.
public sealed class PageBaselineTests
{
    [Theory]
    [InlineData(typeof(GuidesIndexPage), "/", "Guides")]
    [InlineData(typeof(TodosPage), "/todos", "Todos")]
    public void Page_RenderedAtRegisteredPath_EmitsTitleAndPageMarker(Type pageType, string path, string marker)
    {
        var routeState = new RouteState { Path = path };
        // RenderDocument, not Render: the <title> assertion below is about the <head>, which exists only
        // when the document is composed around the app the way a host composes it.
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.NotNull(pageType);
        // <title> now carries data-rask-key="tag:title" so the morph reconciles it by
        // identity across navigations (regression: HeadAssetRegistry head-asset keying).
        Assert.Contains("<title ", html);
        Assert.Contains(marker, html);
    }

    [Theory]
    [InlineData(typeof(GuidesIndexPage))]
    [InlineData(typeof(NotFoundPage))]
    [InlineData(typeof(TodosPage))]
    public void Page_IsRoutableOrTheNotFoundPage(Type pageType)
    {
        // A routable component carries [Route] — repeated once per URL it answers — or is the [NotFound]
        // catch-all. The templates are read at compile time into the route registry, so what is checked
        // here is that the class is declared routable at all.
        var hasRouteAttribute = pageType.GetCustomAttributes<RouteAttribute>().Any();
        var hasNotFound = pageType.GetCustomAttributes<NotFoundAttribute>().Any();

        Assert.True(hasRouteAttribute || hasNotFound,
            $"{pageType.Name} should carry [Route] or be the [NotFound] page");
    }

    [Fact]
    public void NotFoundPage_HasNotFoundAttribute() =>
        Assert.True(typeof(NotFoundPage).GetCustomAttributes<NotFoundAttribute>().Any());

    [Fact]
    public void UnmatchedRoute_RendersNotFoundPage()
    {
        var routeState = new RouteState { Path = "/__no_such_route" };
        var html = RaskTest.Render(new Shared.App(), TestServices.Default(routeState: routeState)).Html;
        Assert.Contains("Page not found", html);
    }
}
