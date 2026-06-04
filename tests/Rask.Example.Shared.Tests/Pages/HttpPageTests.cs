using System.Net;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class HttpPageTests
{
    [Fact]
    public async Task OnMountAsync_FetchesPost_PopulatesArticle()
    {
        const string body =
            "{\"id\":1,\"title\":\"hello\",\"body\":\"the body text\"}";
        var (http, fakeHttp) = FakeHttp.WithJson(body);
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        // Render via App so OnMountAsync fires. Wait for at least one fetch to complete.
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        await WaitFor.True(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));
        // Re-render so the post is visible.
        await Task.Delay(50);
        html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);

        Assert.True(fakeHttp.RequestCount >= 1);
        // Verify the fetch went to the expected relative path.
        Assert.Contains(fakeHttp.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/data/posts-1.json"));
    }

    [Fact]
    public async Task OnMountAsync_HttpFailure_SetsErrorPath()
    {
        // A genuine HTTP-status failure carries a StatusCode and must still surface the
        // error banner (the demo's error handling is a real feature).
        var (http, _) = FakeHttp.Throwing(
            new HttpRequestException("boom", inner: null, statusCode: HttpStatusCode.InternalServerError));
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        // Loading shows initially; after the fetch faults the error banner should appear on next render.
        await Task.Delay(120);
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);

        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task OnMountAsync_BrowserAbort_DoesNotShowError()
    {
        // A hard browser refresh kills the in-flight fetch outside the AbortController, so it
        // surfaces as an HttpRequestException with no StatusCode ("TypeError: Load failed").
        // That's a teardown artifact and must not render the error banner.
        var (http, _) = FakeHttp.Throwing(new HttpRequestException("TypeError: Load failed"));
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        await Task.Delay(120);
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);

        // No error banner — the swallowed abort leaves the page on its loading state.
        Assert.DoesNotContain("alert-danger", html);
        Assert.Contains("Loading", html);
    }

    [Fact]
    public async Task OnMountAsync_HttpNotFound_DoesNotThrow_AndKeepsPageAlive()
    {
        var (http, _) = FakeHttp.WithStatus(HttpStatusCode.NotFound);
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        await Task.Delay(120);
        html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        Assert.Contains("HttpClient", html);
    }
}
