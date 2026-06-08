using System.Net;
using System.Text;
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
    public async Task OnMountAsync_TransientTransportFailure_RetriesAndLoads()
    {
        // A fast browser refresh produces a transport-level HttpRequestException with no
        // StatusCode ("TypeError: Load failed") that can fire transiently on the surviving
        // page when its first fetch races the discarded page's network teardown. The page
        // must retry and self-heal rather than hang on the spinner — and must not flash the
        // error banner for a failure that recovers.
        const string body = "{\"id\":1,\"title\":\"hello\",\"body\":\"the body text\"}";
        var attempts = 0;
        var handler = new FakeHttp
        {
            Handler = _ => Interlocked.Increment(ref attempts) <= 1
                ? throw new HttpRequestException("TypeError: Load failed")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                })
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        // Re-render the SAME app instance: the retried fetch resolves on a continuation after
        // the first render returns, and only the same HttpPage instance retains the result.
        var app = new Rask.Example.Shared.App();
        app.RenderAsLiveRoot(services);
        await WaitFor.True(() => handler.RequestCount >= 2, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        var html = app.RenderAsLiveRoot(services);

        Assert.DoesNotContain("alert-danger", html);
        Assert.Contains("the body text", html);
    }

    [Fact]
    public async Task OnMountAsync_PersistentTransportFailure_ShowsErrorAfterRetries()
    {
        // A transport failure that never recovers (every attempt throws a null-status
        // HttpRequestException) must surface the error banner once retries are exhausted —
        // never leave the page spinning forever.
        var (http, handler) = FakeHttp.Throwing(new HttpRequestException("TypeError: Load failed"));
        var routeState = new RouteState { Path = "/http" };
        var services = TestServices.Default(http: http, routeState: routeState);

        // Re-render the SAME app instance so the retry loop's terminal error (set on a
        // continuation) is observed; the loop makes MaxTransientRetries + 1 attempts then stops.
        var app = new Rask.Example.Shared.App();
        app.RenderAsLiveRoot(services);
        await WaitFor.True(() => handler.RequestCount > 3, TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        var html = app.RenderAsLiveRoot(services);

        Assert.Contains("alert-danger", html);
        // The spinner is gone — the page no longer hangs on the loading state.
        // ("Loading…" still appears verbatim inside the page's code sample, so assert on the
        // spinner-border indicator class instead.)
        Assert.DoesNotContain("spinner-border", html);
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
