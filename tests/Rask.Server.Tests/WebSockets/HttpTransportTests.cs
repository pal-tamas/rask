using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The live protocol over plain HTTP: frames down a Server-Sent Events stream, frames up as POSTs.
/// </summary>
/// <remarks>
///     <para>
///         This exists for a client whose WebSocket never opened — a corporate proxy, a captive portal —
///         because a page that cannot connect is not a page. It carries the same protocol the socket does,
///         at the cost of one request per interaction.
///     </para>
///     <para>
///         Every read here has a deadline. A stream test that waits without one does not fail when the
///         response is buffered rather than streamed; it hangs, and a hung test in a one-minute gate is
///         worse than a red one.
///     </para>
/// </remarks>
public sealed class HttpTransportTests
{
    private const string UserHeader = "X-Test-User";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static void StampUser(IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
            {
                ctx.User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, name!)], "TestCookie"));
            }

            await next(ctx);
        });

    private static RaskTestHost NewHost(TimeSpan? gracePeriod = null) =>
        RaskTestHost.Create<NoOpApp>(
            configureMiddleware: StampUser,
            configureServer: o =>
            {
                if (gracePeriod is { } grace)
                {
                    o.SessionGracePeriod = grace;
                }
            });

    private static async Task<bool> GoneWithinAsync(RaskTestHost host, string sessionId, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            if (host.Store.Peek(sessionId) is null)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return host.Store.Peek(sessionId) is null;
    }

    private static async Task<HttpStatusCode> PostAsync(RaskTestHost host, string path, string generation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new[] { new { type = "navigate", path = "/", query = "" } }),
        };
        request.Headers.Add("Rask-Stream", generation);
        using var response = await host.Http.SendAsync(request);
        return response.StatusCode;
    }

    private static string GenerationOf(string openingFrame) =>
        openingFrame.Split("\"generation\":")[1].TrimEnd('}', ' ');

    /// <summary>
    ///     A tab that closed is not coming back — a reload or a navigation makes a new session — so its session is
    ///     freed when it says so, not after the grace period a dropped connection gets.
    /// </summary>
    [Fact]
    public async Task Leaving_frees_the_session_now_rather_than_after_the_grace_period()
    {
        using var host = NewHost(gracePeriod: TimeSpan.FromMinutes(5));
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            var generation = GenerationOf(await ReadEventAsync(reader));

            Assert.Equal(HttpStatusCode.NoContent, await PostAsync(host, $"/_rask/leave/{sessionId}", generation));

            Assert.True(await GoneWithinAsync(host, sessionId, TimeSpan.FromSeconds(3)),
                "a leaving tab's session should go at once, not after five minutes");
        }
    }

    /// <summary>
    ///     A closing tab tears its stream down while the leave request is on its way, so the stream's cleanup often
    ///     runs first. The leave still names that stream, and nothing has attached since: same tab, gone.
    /// </summary>
    [Fact]
    public async Task A_leave_that_arrives_after_its_stream_ended_still_frees_the_session()
    {
        using var host = NewHost(gracePeriod: TimeSpan.FromMinutes(5));
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        var generation = GenerationOf(await ReadEventAsync(reader));
        reader.Dispose();
        response.Dispose();

        for (var i = 0; i < 150 && host.Store.ConnectedCount > 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, host.Store.ConnectedCount);
        Assert.NotNull(host.Store.Peek(sessionId));

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(host, $"/_rask/leave/{sessionId}", generation));

        Assert.True(await GoneWithinAsync(host, sessionId, TimeSpan.FromSeconds(3)));
    }

    /// <summary>
    ///     A request the server refuses must not be what keeps a detached session alive. Looking the session up used
    ///     to cancel its pending removal before any check ran, and nothing re-armed it — so a stale tab's POST, or
    ///     anyone holding the id, kept it in memory for good, and MaxSessions eventually answered 503.
    /// </summary>
    [Fact]
    public async Task A_refused_post_does_not_keep_a_detached_session_alive()
    {
        using var host = NewHost(gracePeriod: TimeSpan.FromMilliseconds(300));
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        var generation = GenerationOf(await ReadEventAsync(reader));
        reader.Dispose();
        response.Dispose();

        for (var i = 0; i < 150 && host.Store.ConnectedCount > 0; i++)
        {
            await Task.Delay(20);
        }

        // The stream is gone, so this generation is no longer current: refused.
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(host, $"/_rask/send/{sessionId}", generation));

        Assert.True(await GoneWithinAsync(host, sessionId, TimeSpan.FromSeconds(3)),
            "the grace period armed by the stream's end should still remove the session");
    }

    private static async Task<string> StartSessionAsync(RaskTestHost host, string? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        using var response = await host.Http.SendAsync(request);
        return MarkupAssert.SessionId(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     Opens the stream and returns its reader, with the response headers read but the body left open.
    /// </summary>
    private static async Task<(HttpResponseMessage Response, StreamReader Reader)> OpenStreamAsync(
        RaskTestHost host, string sessionId, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/_rask/stream/{sessionId}");
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return (response, new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8));
    }

    /// <summary>Reads one non-empty line, or fails rather than waiting for ever.</summary>
    private static async Task<string> ReadLineAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(Deadline);
        var line = await reader.ReadLineAsync(cts.Token);
        Assert.NotNull(line);
        return line!;
    }

    private static async Task<string> ReadEventAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await ReadLineAsync(reader);
            if (line.Length > 0)
            {
                return line;
            }
        }
    }

    [Fact]
    public async Task The_stream_names_its_generation_and_carries_the_sessions_frames()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            // The stream names itself first, so a POST can say which one it is talking to.
            var first = await ReadEventAsync(reader);
            Assert.StartsWith("data: ", first, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"stream\"", first, StringComparison.Ordinal);

            // …and the session is attached, which is what makes this a live page rather than a download.
            for (var i = 0; i < 100 && host.Store.ConnectedCount == 0; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(1, host.Store.ConnectedCount);
        }
    }

    [Fact]
    public async Task A_session_this_host_never_had_is_told_so_rather_than_left_hanging()
    {
        using var host = NewHost();

        var (response, reader) = await OpenStreamAsync(host, "not-a-session");
        using (response)
        using (reader)
        {
            var frames = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var line = await ReadEventAsync(reader);
                frames.Add(line);
                if (line.Contains("session-unknown", StringComparison.Ordinal))
                {
                    break;
                }
            }

            Assert.Contains(frames, f => f.Contains("\"status\":\"unknown\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_post_from_someone_who_does_not_own_the_session_is_refused()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host, "alice");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/send/{sessionId}")
        {
            Content = JsonContent.Create(new[] { new { type = "navigate", url = "/" } }),
        };
        request.Headers.Add(UserHeader, "bob");
        request.Headers.Add("Rask-Stream", "1");

        using var response = await host.Http.SendAsync(request);

        // The same answer a session this host never had would give, so neither can be probed for.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_post_naming_no_stream_is_refused()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/send/{sessionId}")
        {
            Content = JsonContent.Create(new[] { new { type = "navigate", url = "/" } }),
        };

        using var response = await host.Http.SendAsync(request);

        // A frame that cannot say which page it came from could belong to a tab that has already been
        // replaced — applying it to the current one is exactly what the generation exists to prevent.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_frame_reaches_the_session_over_the_stream_it_names()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            var hello = await ReadEventAsync(reader);
            var generation = hello.Split("\"generation\":")[1].TrimEnd('}', ' ');

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/send/{sessionId}")
            {
                Content = JsonContent.Create(new[] { new { type = "navigate", url = "/" } }),
            };
            request.Headers.Add("Rask-Stream", generation);

            using var accepted = await host.Http.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task A_beacon_from_the_current_tab_ends_its_stream()
    {
        using var host = NewHost();
        var sessionId = await StartSessionAsync(host);

        var (response, reader) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            var hello = await ReadEventAsync(reader);
            var generation = hello.Split("\"generation\":")[1].TrimEnd('}', ' ');

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/leave/{sessionId}");
            request.Headers.Add("Rask-Stream", generation);

            using var left = await host.Http.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);

            // The stream says why it is ending before the body stops: an HTTP response that simply ends is
            // indistinguishable from a dropped link.
            var closing = await ReadEventAsync(reader);
            Assert.Contains("close", closing, StringComparison.Ordinal);
        }
    }
}
