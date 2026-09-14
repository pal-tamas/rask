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

    private static RaskTestHost NewHost() => RaskTestHost.Create<NoOpApp>(configureMiddleware: StampUser);

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
