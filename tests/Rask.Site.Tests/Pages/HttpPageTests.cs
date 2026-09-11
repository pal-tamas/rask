using System.Net;
using System.Text;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

/// <remarks>
///     The retry tests run the demo on a <see cref="ManualClock" /> and move it forward on every poll, instead of
///     sleeping through the loop's real 150 ms delays and 5 s deadlines (#1067). That takes the timers off the
///     thread pool, and makes a fetch that never settles testable at all: four deadlines would otherwise be 20 s
///     of wall clock. What it cannot remove is the framework's own hop. <c>LifecycleSyncContext</c> resumes
///     every await in a lifecycle hook through <c>Task.Run</c>, so each retry still takes one thread-pool turn,
///     and the wait's budget bounds how long those turns may take.
/// </remarks>
public sealed partial class HttpPageTests : global::Rask.Core.RaskMarkup
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
        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, TimeProvider.System));
        await WaitFor.True(
            () => page.Render().Contains("the body text", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            "the fetched post never rendered");
        var html = page.Render();

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

        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, TimeProvider.System));
        // Loading shows initially; after the fetch faults the error banner should appear on next render.
        await Task.Delay(120);
        var html = page.Render();

        Assert.Contains("alert-error", html, StringComparison.Ordinal);
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
            Action = _ => Interlocked.Increment(ref attempts) <= 1
                ? throw new HttpRequestException("TypeError: Load failed")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                })
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var clock = new ManualClock();

        // The fetch + retry self-heal lives in HttpFetchDemo (the page just embeds its source).
        // Drive the demo directly through LiveHost so we assert on its rendered RESULT, not the
        // page's source-code pane (which contains the alert's own class names as literal text).
        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, clock));
        await WaitFor.True(
            () => AdvanceAndRender(page, clock).Contains("the body text", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            "the retried fetch never rendered its body");
        var html = page.Render();

        Assert.DoesNotContain("alert-error", html, StringComparison.Ordinal);
        Assert.Contains("the body text", html);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task OnMountAsync_PersistentTransportFailure_ShowsErrorAfterRetries()
    {
        // A transport failure that never recovers (every attempt throws a null-status
        // HttpRequestException) must surface the error banner once retries are exhausted —
        // never leave the page spinning forever.
        var (http, handler) = FakeHttp.Throwing(new HttpRequestException("TypeError: Load failed"));
        var clock = new ManualClock();

        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, clock));

        // The control. The first attempt fails inside the render, and the loop then parks on a retry delay
        // only this clock can end: however long the test waits here, nothing moves until it moves the clock.
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("Loading", page.Render(), StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);

        await WaitFor.True(
            () => AdvanceAndRender(page, clock).Contains("alert-error", StringComparison.Ordinal),
            TimeSpan.FromSeconds(6),
            "the exhausted retry loop never surfaced its error banner");
        var html = page.Render();

        Assert.Contains("alert-error", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading", html, StringComparison.Ordinal);
        // MaxTransientRetries + 1 attempts, then it stops.
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task OnMountAsync_FetchThatNeverSettles_TimesOutAfterRetries()
    {
        // The failure the per-attempt deadline exists for: no exception, no response, an await that would
        // never return. Each attempt must give up on its own deadline and retry, and the last must say what
        // it was waiting for rather than "A task was canceled."
        var handler = new NeverSettles();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var clock = new ManualClock();

        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, clock));
        await WaitFor.True(
            () => AdvanceAndRender(page, clock).Contains("alert-error", StringComparison.Ordinal),
            TimeSpan.FromSeconds(6),
            "four attempts that never settled never surfaced their timeout");
        var html = page.Render();

        Assert.Contains("The request did not complete within 5s.", html, StringComparison.Ordinal);
        Assert.Equal(4, handler.Attempts);
    }

    [Fact]
    public async Task OnMountAsync_HttpNotFound_SurfacesError_DoesNotThrow()
    {
        // A 404 carries a real StatusCode, so the demo surfaces the error banner (not a retry) and
        // never throws out of the lifecycle — the page/guide stays alive around it.
        var (http, _) = FakeHttp.WithStatus(HttpStatusCode.NotFound);

        var page = RaskTest.Render(() => HttpFetchDemo, Services(http, TimeProvider.System));
        await Task.Delay(120);
        var html = page.Render();

        Assert.Contains("alert-error", html, StringComparison.Ordinal);
    }

    private static IServiceProvider Services(HttpClient http, TimeProvider time) =>
        LiveHost.Services((typeof(HttpClient), (object)http), (typeof(TimeProvider), (object)time));

    // One poll of a wait: move the clock a second, past any retry delay the loop has set and a fifth of the way
    // through an attempt's deadline, then look at what rendered. The loop sets its next timer on a thread-pool
    // turn, so one large advance up front would fire nothing it had not set yet.
    private static string AdvanceAndRender(RenderedComponent page, ManualClock clock)
    {
        clock.Advance(TimeSpan.FromSeconds(1));
        return page.Render();
    }
}

// A fetch that neither responds nor fails: it settles only when its token is cancelled.
file sealed class NeverSettles : HttpMessageHandler
{
    public int Attempts;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Attempts);
        var never = new TaskCompletionSource<HttpResponseMessage>();
        ct.Register(() => never.TrySetCanceled(ct));
        return never.Task;
    }
}
