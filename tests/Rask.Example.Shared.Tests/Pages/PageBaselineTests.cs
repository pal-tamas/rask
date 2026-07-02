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
    [InlineData(typeof(HomePage), "/", "Welcome")]
    [InlineData(typeof(LiveTickerPage), "/realtime/BTC", "BTC live ticker")]
    [InlineData(typeof(TablePage), "/table", "Data table")]
    [InlineData(typeof(TodosPage), "/todos", "Todos")]
    public void Page_RenderedAtRegisteredPath_EmitsTitleAndPageMarker(Type pageType, string path, string marker)
    {
        var routeState = new RouteState { Path = path };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.NotNull(pageType);
        // <title> now carries data-rask-key="tag:title" so the morph reconciles it by
        // identity across navigations (regression: HeadAssetRegistry head-asset keying).
        Assert.Contains("<title ", html);
        Assert.Contains(marker, html);
    }

    [Theory]
    [InlineData(typeof(HomePage))]
    [InlineData(typeof(LiveTickerPage))]
    [InlineData(typeof(NotFoundPage))]
    [InlineData(typeof(TablePage))]
    [InlineData(typeof(TodosPage))]
    public void Page_HasExpectedRouteOrNotFoundAttribute(Type pageType)
    {
        var hasRoute = pageType.GetCustomAttributes<RouteAttribute>().Any();
        var hasNotFound = pageType.GetCustomAttributes<NotFoundAttribute>().Any();
        Assert.True(hasRoute || hasNotFound,
            $"{pageType.Name} should have [Route] or [NotFound]");
    }

    [Fact]
    public void NotFoundPage_HasNotFoundAttribute() =>
        Assert.True(typeof(NotFoundPage).GetCustomAttributes<NotFoundAttribute>().Any());

    [Fact]
    public void UnmatchedRoute_RendersNotFoundPage()
    {
        var routeState = new RouteState { Path = "/__no_such_route" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));
        Assert.Contains("Page not found", html);
    }
}
