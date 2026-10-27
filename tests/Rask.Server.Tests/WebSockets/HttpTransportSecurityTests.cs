using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The HTTP transport's guards: what a request has to be before it reaches a session.
/// </summary>
/// <remarks>
///     A socket is checked once, at its upgrade. Every one of these is a separate request that could come from
///     anywhere, so every one of them is checked — and each case here is a way a request used to get further
///     than it should have.
/// </remarks>
public sealed class HttpTransportSecurityTests
{
    /// <summary>A runtime from before the header — a tab open across the deploy — still reaches its session by the path.</summary>
    [Fact]
    public async Task A_stream_that_names_its_session_in_the_path_still_attaches()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var sessionId = await StartSessionAsync(host);

        using var response = await host.Http.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/_rask/stream/{sessionId}"), HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), Encoding.UTF8);
        var opening = await EventStream.OpeningFrameAsync(reader);

        Assert.Contains($"\"session\":\"{sessionId}\"", opening, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The session id drives the session, so it never goes in a URL: ASP.NET's request log, a reverse proxy's access
    ///     log and a browser's network panel all write the path down. It rides the <c>Rask-Session</c> header.
    /// </summary>
    [Fact]
    public async Task A_tab_on_the_http_fallback_never_puts_its_session_id_in_a_request_path()
    {
        var paths = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var host = RaskTestHost.Create<NoOpApp>(configureMiddleware: app => app.Use(async (ctx, next) =>
        {
            paths.Enqueue(ctx.Request.Path + ctx.Request.QueryString);
            await next(ctx);
        }));
        var sessionId = await StartSessionAsync(host);

        await using (var tab = await LiveTestConnection.OpenAsync(host, LiveTransportKind.Http, sessionId))
        {
            await tab.SettledAsync();
        }

        Assert.Contains(paths, static p => p.StartsWith("/_rask/stream", StringComparison.Ordinal));
        Assert.Contains(paths, static p => p.StartsWith("/_rask/send", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.Contains(sessionId, StringComparison.Ordinal));
    }

    private static async Task<string> StartSessionAsync(RaskTestHost host)
    {
        using var response = await host.Http.GetAsync("/");
        return MarkupAssert.SessionId(await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpResponseMessage Response, StreamReader Reader, string Generation)> OpenStreamAsync(
        RaskTestHost host, string sessionId)
    {
        var stream = new HttpRequestMessage(HttpMethod.Get, "/_rask/stream");
        stream.Headers.Add("Rask-Session", sessionId);
        var response = await host.Http.SendAsync(stream, HttpCompletionOption.ResponseHeadersRead);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8);
        return (response, reader, await EventStream.GenerationAsync(reader));
    }

    private static async Task<List<string>> ReadUntilAsync(StreamReader reader, string needle, int max = 12)
    {
        var lines = new List<string>();
        for (var i = 0; i < max; i++)
        {
            var line = await EventStream.NextAsync(reader);
            lines.Add(line);
            if (line.Contains(needle, StringComparison.Ordinal))
            {
                break;
            }
        }

        return lines;
    }

    /// <summary>
    ///     A session id is request input. It used to be spliced into the hello as JSON text, so an id carrying a
    ///     quote could close the string and add structure of its own.
    /// </summary>
    [Fact]
    public async Task A_session_id_that_looks_like_json_is_only_ever_an_id()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var hostile = "x\",\"resume\":\"forged";

        var (response, reader, _) = await OpenStreamAsync(host, hostile);
        using (response)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Nothing attached and nothing resumed: an id this host never had is unknown, however it is spelled.
            var lines = await ReadUntilAsync(reader, "\"status\":\"unknown\"");
            Assert.Contains(lines, l => l.Contains("\"status\":\"unknown\"", StringComparison.Ordinal));
            Assert.Equal(0, host.Store.ConnectedCount);
        }
    }

    [Fact]
    public async Task A_post_that_is_not_json_is_refused()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var sessionId = await StartSessionAsync(host);

        var (response, reader, generation) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/send")
            {
                Content = new StringContent("[]", Encoding.UTF8, "text/plain"),
            };
            request.Headers.Add("Rask-Session", sessionId);
            request.Headers.Add("Rask-Stream", generation);

            using var refused = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
        }
    }

    /// <summary>
    ///     The size cap holds for a body that never states its length — the one a Content-Length check alone would
    ///     have read in full.
    /// </summary>
    [Fact]
    public async Task A_body_past_the_frame_cap_is_refused_even_without_a_length()
    {
        using var host = RaskTestHost.Create<NoOpApp>(configureServer: o => o.MaxInboundFrameBytes = 64);
        var sessionId = await StartSessionAsync(host);

        var (response, reader, generation) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            var refused = await PostPastTheCapAsync(host, sessionId, generation);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused);
        }
    }

    /// <summary>
    ///     A page repainted while no connection was attached is sent that repaint as the stream opens, ahead of the
    ///     frame that names the stream. The name still comes, and a POST under it is held to the cap like any other.
    /// </summary>
    [Fact]
    public async Task A_body_past_the_frame_cap_is_refused_on_a_stream_that_opened_with_a_repaint_owed()
    {
        using var host = RaskTestHost.Create<ShownCountApp>(
            configureServices: s => s.AddSingleton<ShownCount>(),
            configureServer: o => o.MaxInboundFrameBytes = 64);
        var sessionId = await StartSessionAsync(host);
        host.Server.Services.GetRequiredService<ShownCount>().Value = 1;
        await host.Store.Peek(sessionId)!.View.StateHasChangedAsync();

        var (response, reader, generation) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            var refused = await PostPastTheCapAsync(host, sessionId, generation);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused);
        }
    }

    // Twenty frames in a body that never states its length, under a 64-byte cap.
    private static async Task<HttpStatusCode> PostPastTheCapAsync(RaskTestHost host, string sessionId, string generation)
    {
        var big = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Repeat("{\"type\":\"noop\"}", 20)) + "]");
        var content = new StreamContent(new UnknownLengthStream(big));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/send") { Content = content };
        request.Headers.Add("Rask-Session", sessionId);
        request.Headers.Add("Rask-Stream", generation);
        request.Headers.TransferEncodingChunked = true;

        using var answer = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return answer.StatusCode;
    }

    /// <summary>
    ///     The socket's frame-rate cap applies over POST too. A flood is answered, and the stream is ended with a
    ///     reason the client can read — the same outcome as a socket closed for the same thing.
    /// </summary>
    [Fact]
    public async Task A_flood_of_frames_is_refused_and_ends_the_stream()
    {
        using var host = RaskTestHost.Create<NoOpApp>(configureServer: o => o.MaxInboundFramesPerSecond = 3);
        var sessionId = await StartSessionAsync(host);

        var (response, reader, generation) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/send")
            {
                Content = JsonContent.Create(Enumerable.Repeat(new { type = "noop" }, 10).ToArray()),
            };
            request.Headers.Add("Rask-Session", sessionId);
            request.Headers.Add("Rask-Stream", generation);

            using var refused = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

            var lines = await ReadUntilAsync(reader, "frame rate");
            Assert.Contains(lines, l => l.Contains("frame rate", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_tab_that_leaves_ends_its_stream_as_an_ordinary_goodbye()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var sessionId = await StartSessionAsync(host);

        var (response, reader, generation) = await OpenStreamAsync(host, sessionId);
        using (response)
        using (reader)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/leave");
            request.Headers.Add("Rask-Session", sessionId);
            request.Headers.Add("Rask-Stream", generation);

            using var left = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);

            var lines = await ReadUntilAsync(reader, "leave");
            Assert.Contains(lines, l => l.Contains("leave", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("GET", "/_rask/stream")]
    [InlineData("POST", "/_rask/send")]
    [InlineData("POST", "/_rask/leave")]
    public async Task A_cross_site_request_is_refused(string method, string path)
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var sessionId = await StartSessionAsync(host);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Rask-Session", sessionId);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var refused = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>A body that will not say how long it is, so the server has to count.</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
