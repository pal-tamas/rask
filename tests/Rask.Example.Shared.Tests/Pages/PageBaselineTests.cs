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
    [InlineData(typeof(BackgroundServicePage), "/background", "Background service")]
    [InlineData(typeof(BoomPage), "/boom", "Error boundary")]
    [InlineData(typeof(CancellationPage), "/cancellation", "Cancellation")]
    [InlineData(typeof(ComponentsPage), "/components", "User components")]
    [InlineData(typeof(DisposalPage), "/disposal", "Disposal")]
    [InlineData(typeof(DownloadPage), "/download", "File download")]
    [InlineData(typeof(EventsPage), "/events", "Events")]
    [InlineData(typeof(HttpPage), "/http", "HttpClient")]
    [InlineData(typeof(LifecyclePage), "/lifecycle", "Lifecycle")]
    [InlineData(typeof(LiveTickerPage), "/realtime/BTC", "BTC live ticker")]
    [InlineData(typeof(NavigatorPage), "/navigator", "Navigator")]
    [InlineData(typeof(PrimitivesPage), "/primitives", "Primitives")]
    [InlineData(typeof(PropsPage), "/props", "Universal props")]
    [InlineData(typeof(RoutingPage), "/routing", "Routing")]
    [InlineData(typeof(ScopedCssPage), "/scoped-css", "Scoped CSS")]
    [InlineData(typeof(TablePage), "/table", "Data table")]
    [InlineData(typeof(TagsPage), "/tags", "Tag factories")]
    [InlineData(typeof(ToastPage), "/toast", "Toast")]
    [InlineData(typeof(TodosPage), "/todos", "Todos")]
    [InlineData(typeof(UploadPage), "/upload", "File upload")]
    [InlineData(typeof(UserDetailPage), "/users/42", "User #42")]
    [InlineData(typeof(VirtualizePage), "/virtualize", "Virtualize")]
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
    [InlineData(typeof(BackgroundServicePage))]
    [InlineData(typeof(BoomPage))]
    [InlineData(typeof(CancellationPage))]
    [InlineData(typeof(ComponentsPage))]
    [InlineData(typeof(DisposalPage))]
    [InlineData(typeof(DownloadPage))]
    [InlineData(typeof(EventsPage))]
    [InlineData(typeof(HttpPage))]
    [InlineData(typeof(LifecyclePage))]
    [InlineData(typeof(LiveTickerPage))]
    [InlineData(typeof(NavigatorPage))]
    [InlineData(typeof(NotFoundPage))]
    [InlineData(typeof(PrimitivesPage))]
    [InlineData(typeof(PropsPage))]
    [InlineData(typeof(RoutingPage))]
    [InlineData(typeof(ScopedCssPage))]
    [InlineData(typeof(TablePage))]
    [InlineData(typeof(TagsPage))]
    [InlineData(typeof(ToastPage))]
    [InlineData(typeof(TodosPage))]
    [InlineData(typeof(UploadPage))]
    [InlineData(typeof(UserDetailPage))]
    [InlineData(typeof(VirtualizePage))]
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
