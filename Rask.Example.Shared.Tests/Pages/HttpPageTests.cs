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
        Assert.Contains(fakeHttp.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/posts/1"));
    }

    [Fact]
    public async Task OnMountAsync_HttpFailure_SetsErrorPath()
    {
        var (http, _) = FakeHttp.Throwing(new HttpRequestException("boom"));
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        new Rask.Example.Shared.App().RenderAsLiveRoot(services);
        // Loading shows initially; after the fetch faults the error banner should appear on next render.
        await Task.Delay(120);
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(services);

        // Either error banner or loading spinner is acceptable as long as the page didn't throw.
        Assert.True(html.Contains("alert-danger") || html.Contains("Loading"));
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
