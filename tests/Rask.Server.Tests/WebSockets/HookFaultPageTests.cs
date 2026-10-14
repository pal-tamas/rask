using System.Net;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A lifecycle hook that faults with no boundary of the app's around it used to be a log line and a page left
// on its skeleton. It is the error page a render fault is: on the first response when the hook faults before
// that is sent, and in the frame that follows when the page was already painted.
[Collection("HostEnvironment")]
public sealed class HookFaultPageTests
{
    private const string Heading = "Something went wrong";
    private const string Sentence = "The problem has been recorded. If it keeps happening, quote this reference: ";

    // Longer than any wait here may last: a first response is sent because its hook ended, never because time ran out.
    private static void Patient(RaskServerOptions o) => o.QuiescenceTimeout = TimeSpan.FromMinutes(5);

    // Over while the test still holds the read.
    private static void Brief(RaskServerOptions o) => o.QuiescenceTimeout = TimeSpan.FromMilliseconds(40);

    [Theory]
    [InlineData("/hook/mount")]
    [InlineData("/hook/updated")]
    [InlineData("/hook/rendered")]
    [InlineData("/hook/deep")]
    [InlineData("/hook-layout/page")]
    public async Task A_first_request_whose_hook_faults_before_its_first_await_answers_500_with_the_error_page(string path)
    {
        using var host = Host(Patient);

        var response = await host.Http.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(Heading, html, StringComparison.Ordinal);
        Assert.Contains(Sentence, html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/hook/mount")]
    [InlineData("/hook/updated")]
    [InlineData("/hook/deep")]
    [InlineData("/hook-layout/page")]
    public async Task A_first_request_whose_hook_faults_after_an_await_answers_500_with_the_error_page(string path)
    {
        using var host = Host(Patient);
        var store = Store(host);
        store.Hold();

        var pending = host.Http.GetAsync(path, TestContext.Current.CancellationToken);
        await store.ReadStarted();
        store.Release();
        var response = await pending;
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(Heading, html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_request_in_development_names_the_exception_the_hook_threw()
    {
        using var host = Host(Patient, "Development");

        var response = await host.Http.GetAsync("/hook/mount", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(Heading, html, StringComparison.Ordinal);
        Assert.Contains(nameof(HookFaultException), html, StringComparison.Ordinal);
        Assert.Contains("hunter2", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_hook_inside_a_boundary_the_app_wrote_trips_that_boundary_and_the_page_answers_200(bool afterAnAwait)
    {
        using var host = Host(Patient);
        var store = Store(host);
        if (afterAnAwait)
        {
            store.Hold();
        }

        var pending = host.Http.GetAsync("/hook/guarded", TestContext.Current.CancellationToken);
        if (afterAnAwait)
        {
            await store.ReadStarted();
            store.Release();
        }

        var response = await pending;
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not load the row", html, StringComparison.Ordinal);
        Assert.Contains(">beside<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Heading, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/hook/mount", false)]
    [InlineData("/hook/mount", true)]
    [InlineData("/hook/updated", false)]
    [InlineData("/hook/updated", true)]
    [InlineData("/hook/rendered", false)]
    [InlineData("/hook/deep", false)]
    [InlineData("/hook/deep", true)]
    [InlineData("/hook-layout/page", false)]
    [InlineData("/hook-layout/page", true)]
    public async Task A_page_reached_in_place_whose_hook_faults_ends_on_the_error_page(string path, bool afterAnAwait)
    {
        using var host = Host(Patient);
        using var ws = await Connect(host);
        var store = Store(host);
        if (afterAnAwait)
        {
            store.Hold();
        }

        await ws.SendJsonAsync(new { type = "navigate", path, query = "" }, TestContext.Current.CancellationToken);
        if (afterAnAwait)
        {
            await store.ReadStarted();
            store.Release();
        }

        // The page is sent at once, so a hook that faults after it is a frame of its own that follows.
        var frames = await Through(ws, Heading);

        var last = frames[^1];
        Assert.Contains(Heading, last, StringComparison.Ordinal);
        Assert.Contains(Sentence, last, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", last, StringComparison.Ordinal);
        Assert.All(frames, frame => Assert.DoesNotContain("hunter2", frame, StringComparison.Ordinal));
        Assert.All(frames, frame => Assert.DoesNotContain("devError", frame, StringComparison.Ordinal));
    }

    // OnRendered runs after the page is sent, so what it awaits is never waited for: it is among these.
    [Theory]
    [InlineData("/hook/mount")]
    [InlineData("/hook/rendered")]
    [InlineData("/hook/deep")]
    [InlineData("/hook-layout/page")]
    public async Task A_page_painted_with_its_skeleton_whose_hook_then_faults_is_repainted_as_the_error_page(string path)
    {
        using var host = Host(Brief);
        using var ws = await Connect(host);
        var store = Store(host);
        store.Hold();

        await ws.SendJsonAsync(new { type = "navigate", path, query = "" }, TestContext.Current.CancellationToken);
        var skeleton = await ws.ReceiveUntilAsync(static frame => frame.Contains("loading", StringComparison.Ordinal), "the page on its skeleton");
        store.Release();
        var after = await ws.ReceiveUntilAsync(static frame => frame.Contains(Heading, StringComparison.Ordinal), "the error page");

        Assert.DoesNotContain(Heading, skeleton, StringComparison.Ordinal);
        Assert.Contains(Sentence, after, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", after, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_response_sent_on_its_skeleton_is_repainted_as_the_error_page_once_the_hook_faults()
    {
        using var host = Host(Brief);
        var store = Store(host);
        store.Hold();

        var response = await host.Http.GetAsync("/hook/mount", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        store.Release();
        var after = await ws.ReceiveUntilAsync(static frame => frame.Contains(Heading, StringComparison.Ordinal), "the error page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("loading", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", after, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_reached_in_place_in_development_shows_the_exception_on_the_error_page()
    {
        using var host = Host(Patient, "Development");
        using var ws = await Connect(host);
        var store = Store(host);
        store.Hold();

        await ws.SendJsonAsync(new { type = "navigate", path = "/hook/mount", query = "" }, TestContext.Current.CancellationToken);
        await store.ReadStarted();
        store.Release();
        var frame = (await Through(ws, Heading))[^1];

        Assert.Contains(Heading, frame, StringComparison.Ordinal);
        Assert.Contains(nameof(HookFaultException), frame, StringComparison.Ordinal);
        Assert.DoesNotContain(">loading<", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("\"devError\"", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_reached_in_place_whose_guarded_hook_faults_keeps_the_page_and_shows_the_apps_fallback()
    {
        using var host = Host(Patient);
        using var ws = await Connect(host);
        var store = Store(host);
        store.Hold();

        await ws.SendJsonAsync(new { type = "navigate", path = "/hook/guarded", query = "" }, TestContext.Current.CancellationToken);
        await store.ReadStarted();
        store.Release();
        var frame = (await Through(ws, "could not load the row"))[^1];

        Assert.Contains("could not load the row", frame, StringComparison.Ordinal);
        Assert.Contains("beside", frame, StringComparison.Ordinal);
        Assert.DoesNotContain(Heading, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Going_to_another_page_from_the_error_page_shows_that_page()
    {
        using var host = Host(Patient);
        using var ws = await Connect(host);
        await ws.SendJsonAsync(new { type = "navigate", path = "/hook/mount", query = "" }, TestContext.Current.CancellationToken);
        var faulted = (await Through(ws, Heading))[^1];

        await ws.SendJsonAsync(new { type = "navigate", path = "/hook/home", query = "" }, TestContext.Current.CancellationToken);
        var home = (await Through(ws, ">home<"))[^1];

        Assert.Contains(Heading, faulted, StringComparison.Ordinal);
        Assert.Contains(">home<", home, StringComparison.Ordinal);
        Assert.DoesNotContain(Heading, home, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/hook/mount")]
    [InlineData("/hook/updated")]
    [InlineData("/hook/deep")]
    [InlineData("/hook-layout/page")]
    public async Task Try_again_on_the_error_page_runs_the_hook_again_and_shows_what_it_loaded(string path)
    {
        using var host = Host(Patient);
        using var ws = await Connect(host);
        await ws.SendJsonAsync(new { type = "navigate", path, query = "" }, TestContext.Current.CancellationToken);
        var faulted = await ErrorPageSettled(ws);
        Store(host).Mended = true;

        await ws.SendJsonAsync(new { id = TryAgain(faulted) }, TestContext.Current.CancellationToken);
        // A diff writes the text bare, a whole page between tags: either way, the row.
        var again = (await Through(ws, "the row"))[^1];

        Assert.Contains("the row", again, StringComparison.Ordinal);
        Assert.DoesNotContain(Heading, again, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Try_again_while_the_read_still_throws_shows_the_error_page_again_and_never_the_skeleton()
    {
        using var host = Host(Patient);
        using var ws = await Connect(host);
        await ws.SendJsonAsync(new { type = "navigate", path = "/hook/mount", query = "" }, TestContext.Current.CancellationToken);
        var faulted = await ErrorPageSettled(ws);

        await ws.SendJsonAsync(new { id = TryAgain(faulted) }, TestContext.Current.CancellationToken);
        var again = (await ws.SettledAsync())[^1];

        Assert.Contains(Heading, again, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", again, StringComparison.Ordinal);
    }

    // The error page as it stands once the navigation has finished: its handlers are the ones a click reaches.
    private static async Task<string> ErrorPageSettled(WebSocket ws)
    {
        var shown = (await Through(ws, Heading))[^1];
        return (await ws.SettledAsync()).LastOrDefault(static f => f.Contains("Try again", StringComparison.Ordinal)) ?? shown;
    }

    // Every frame up to and with the first that shows the text.
    private static async Task<List<string>> Through(WebSocket ws, string text)
    {
        var frames = new List<string>();
        while (frames.Count == 0 || !frames[^1].Contains(text, StringComparison.Ordinal))
        {
            frames.Add(await ws.ReceiveTextAsync());
        }

        return frames;
    }

    private static string TryAgain(string frame)
    {
        var handler = Regex.Match(frame, "data-rask-on-click=\\\\\"(h\\d+)\\\\\"[^>]*>Try again").Groups[1].Value;
        Assert.NotEmpty(handler);
        return handler;
    }

    private static RaskTestHost Host(Action<RaskServerOptions> configure, string environment = "Production") =>
        RaskTestHost.Create<HookFaultApp>(
            configureServices: static services => services.AddSingleton<HookFaultStore>(),
            configureServer: configure,
            diffMode: LiveDiffMode.DisabledFull,
            environment: environment);

    private static HookFaultStore Store(RaskTestHost host) => host.Server.Services.GetRequiredService<HookFaultStore>();

    private static async Task<WebSocket> Connect(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/hook/home", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return ws;
    }
}
