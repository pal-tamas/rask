using System.Net;
using System.Text;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class HttpPageTests
{
    [Fact]
    public async Task OnMountAsync_FetchesPost_PopulatesArticle()
    {
        const string body =
            "{\"id\":1,\"title\":\"hello\",\"body\":\"the body text\"}";
        var (http, fakeHttp) = FakeHttp.WithJson(body);

        // Drive HttpFetchDemo directly through LiveHost — its standalone /http page was folded into
        // docs/http-and-files.md. Re-rendering the SAME host preserves the demo instance so the
        // awaited fetch's continuation result is observed.
        var host = new LiveHost(() => HttpFetchDemo(), LiveHost.Services((typeof(HttpClient), (object)http)));
        host.RenderAsLiveRoot();
        await WaitFor.True(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        var html = host.RenderAsLiveRoot();

        Assert.True(fakeHttp.RequestCount >= 1);
        // Verify the fetch went to the expected relative path, and the post rendered.
        Assert.Contains(fakeHttp.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/data/posts-1.json"));
        Assert.Contains("the body text", html);
    }

    [Fact]
    public async Task OnMountAsync_HttpFailure_SetsErrorPath()
    {
        // A genuine HTTP-status failure carries a StatusCode and must still surface the
        // error banner (the demo's error handling is a real feature).
        var (http, _) = FakeHttp.Throwing(
            new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));

        var host = new LiveHost(() => HttpFetchDemo(), LiveHost.Services((typeof(HttpClient), (object)http)));
        host.RenderAsLiveRoot();
        // Loading shows initially; after the fetch faults the error banner should appear on next render.
        await Task.Delay(120);
        var html = host.RenderAsLiveRoot();

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

        // The fetch + retry self-heal lives in HttpFetchDemo (the page just embeds its source).
        // Drive the demo directly through LiveHost so we assert on its rendered RESULT, not the
        // page's source-code pane (which now contains "alert-danger"/"spinner-border" as literal
        // text). Re-rendering the SAME host preserves the demo instance, so the retried fetch's
        // continuation result is observed.
        var host = new LiveHost(() => HttpFetchDemo(), LiveHost.Services((typeof(HttpClient), (object)http)));
        host.RenderAsLiveRoot();
        await WaitFor.True(() => handler.RequestCount >= 2, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        var html = host.RenderAsLiveRoot();

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

        // Drive HttpFetchDemo directly (it owns the retry loop); the page only embeds its source.
        // Re-rendering the SAME host preserves the demo instance so the retry loop's terminal
        // error (set on a continuation) is observed; the loop makes MaxTransientRetries + 1
        // attempts then stops.
        var host = new LiveHost(() => HttpFetchDemo(), LiveHost.Services((typeof(HttpClient), (object)http)));
        host.RenderAsLiveRoot();
        await WaitFor.True(() => handler.RequestCount > 3, TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        var html = host.RenderAsLiveRoot();

        Assert.Contains("alert-danger", html);
        // The spinner is gone — the demo no longer hangs on the loading state. Asserting on the
        // demo's rendered result (not the page) keeps the spinner-border check meaningful.
        Assert.DoesNotContain("spinner-border", html);
    }

    [Fact]
    public async Task OnMountAsync_HttpNotFound_SurfacesError_DoesNotThrow()
    {
        // A 404 carries a real StatusCode, so the demo surfaces the error banner (not a retry) and
        // never throws out of the lifecycle — the page/guide stays alive around it.
        var (http, _) = FakeHttp.WithStatus(HttpStatusCode.NotFound);

        var host = new LiveHost(() => HttpFetchDemo(), LiveHost.Services((typeof(HttpClient), (object)http)));
        host.RenderAsLiveRoot();
        await Task.Delay(120);
        var html = host.RenderAsLiveRoot();

        Assert.Contains("alert-danger", html);
    }
}
