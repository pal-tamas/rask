using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>The two ways a browser tab carries the live protocol.</summary>
public enum LiveTransportKind
{
    WebSocket,
    Http,
}

/// <summary>
///     One tab's live connection, whichever transport carries it, so a protocol test runs over both.
/// </summary>
/// <remarks>
///     A test that only ever spoke WebSocket would pass while the HTTP fallback quietly diverged — and the
///     fallback's users are exactly the ones nobody on a developer's network ever is. The point of this seam is
///     that the ordering, malformed-frame, backpressure, shutdown and resume contracts are asserted once and
///     hold for both.
/// </remarks>
internal interface ILiveTestConnection : IAsyncDisposable
{
    /// <summary>Whether the server has not yet ended this connection.</summary>
    bool IsOpen { get; }

    Task SendJsonAsync(object payload);

    /// <summary>Sends text exactly as given — for frames that are deliberately not valid JSON.</summary>
    Task SendRawAsync(string frame);

    /// <summary>The next frame from the server, or <c>null</c> if none arrived in time or the connection ended.</summary>
    Task<string?> TryReceiveTextAsync(TimeSpan timeout);

    /// <summary>
    ///     Waits for the server to end the connection and returns the reason it gave, or <c>null</c> if it ended
    ///     without one (an abort) or did not end in time.
    /// </summary>
    Task<string?> TryReceiveCloseReasonAsync(TimeSpan timeout);
}

internal static class LiveTestConnection
{
    /// <summary>Both transports, for <c>[MemberData]</c>.</summary>
    public static TheoryData<LiveTransportKind> Transports() => [LiveTransportKind.WebSocket, LiveTransportKind.Http];

    /// <summary>
    ///     Connects to <paramref name="sessionId" /> the way the browser runtime does: a socket and a <c>hello</c>,
    ///     or a stream that names the session in its URL and the resume record in a header.
    /// </summary>
    public static async Task<ILiveTestConnection> OpenAsync(
        RaskTestHost host, LiveTransportKind kind, string sessionId, string? resume = null) =>
        kind switch
        {
            LiveTransportKind.WebSocket => await WebSocketConnection.OpenAsync(host, sessionId, resume),
            LiveTransportKind.Http => await HttpConnection.OpenAsync(host, sessionId, resume),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private sealed class WebSocketConnection(WebSocket ws) : ILiveTestConnection
    {
        public static async Task<ILiveTestConnection> OpenAsync(RaskTestHost host, string sessionId, string? resume)
        {
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            if (resume is null)
            {
                await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            }
            else
            {
                await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume });
            }

            return new WebSocketConnection(ws);
        }

        public bool IsOpen => ws.State == WebSocketState.Open;

        public Task SendJsonAsync(object payload) => ws.SendJsonAsync(payload);

        public async Task SendRawAsync(string frame) =>
            await ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);

        public Task<string?> TryReceiveTextAsync(TimeSpan timeout) => ws.TryReceiveTextAsync(timeout);

        public async Task<string?> TryReceiveCloseReasonAsync(TimeSpan timeout) =>
            await ws.TryReceiveCloseAsync(timeout) is { } close ? close.Reason : null;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch
            {
                // Already gone — which is what several tests are for.
            }

            ws.Dispose();
        }
    }

    /// <summary>
    ///     The stream read by a pump into a channel, so a receive that times out never abandons a read half-way
    ///     through the response body — a cancelled <see cref="StreamReader.ReadLineAsync(CancellationToken)" />
    ///     leaves the reader unusable, and every later receive would then fail for a reason unrelated to the test.
    /// </summary>
    private sealed class HttpConnection : ILiveTestConnection
    {
        private static readonly TimeSpan OpenDeadline = TimeSpan.FromSeconds(5);

        private readonly RaskTestHost _host;

        // Replaced by the session the stream names, as the browser runtime does: a resume record rebuilds a lost
        // session under a new id, and a POST to the old one would be answered 404.
        private string _sessionId;
        private readonly HttpResponseMessage _response;
        private readonly CancellationTokenSource _pumpCts = new();
        private readonly Channel<string> _frames = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<string> _generation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _pump;

        private HttpConnection(RaskTestHost host, string sessionId, HttpResponseMessage response, Stream body)
        {
            _host = host;
            _sessionId = sessionId;
            _response = response;
            _pump = PumpAsync(body);
        }

        public static async Task<ILiveTestConnection> OpenAsync(RaskTestHost host, string sessionId, string? resume)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/_rask/stream/{Uri.EscapeDataString(sessionId)}");
            request.Headers.Accept.ParseAdd("text/event-stream");
            if (resume is not null)
            {
                request.Headers.Add("Rask-Resume", resume);
            }

            var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var connection = new HttpConnection(host, sessionId, response, await response.Content.ReadAsStreamAsync());

            // Open once the stream has named its generation, exactly as the browser runtime counts it.
            await connection._generation.Task.WaitAsync(OpenDeadline);
            return connection;
        }

        public bool IsOpen => !_closed.Task.IsCompleted;

        public Task SendJsonAsync(object payload) => PostAsync(JsonSerializer.Serialize(payload));

        public Task SendRawAsync(string frame) => PostAsync(frame);

        public async Task<string?> TryReceiveTextAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await _frames.Reader.ReadAsync(cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                return null;
            }
        }

        public async Task<string?> TryReceiveCloseReasonAsync(TimeSpan timeout)
        {
            try
            {
                return await _closed.Task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        ///     One frame per POST, awaited — the browser batches what piles up behind an in-flight POST, which
        ///     preserves the same order one awaited POST at a time does. The status is not asserted: a refusal
        ///     ends the stream with a reason, and that is what the tests observe, as the runtime does.
        /// </summary>
        private async Task PostAsync(string frame)
        {
            var generation = await _generation.Task;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/send/{Uri.EscapeDataString(_sessionId)}")
            {
                Content = new StringContent("[" + frame + "]", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Rask-Stream", generation);

            using var response = await _host.Http.SendAsync(request);
        }

        private async Task PumpAsync(Stream body)
        {
            string? closeReason = null;
            try
            {
                using var reader = new StreamReader(body, Encoding.UTF8);
                var eventName = string.Empty;
                var data = new StringBuilder();
                var hasData = false;

                while (await reader.ReadLineAsync(_pumpCts.Token) is { } line)
                {
                    if (line.Length == 0)
                    {
                        if (hasData)
                        {
                            if (eventName == "close")
                            {
                                closeReason = data.ToString();
                                break;
                            }

                            Dispatch(data.ToString());
                        }

                        eventName = string.Empty;
                        data.Clear();
                        hasData = false;
                        continue;
                    }

                    if (line[0] == ':')
                    {
                        continue;
                    }

                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        eventName = line["event: ".Length..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        if (hasData)
                        {
                            data.Append('\n');
                        }

                        data.Append(line.AsSpan("data: ".Length));
                        hasData = true;
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Disposed, or the response ended under the reader: an end without a reason.
            }
            finally
            {
                _generation.TrySetException(new InvalidOperationException("the stream ended before naming its generation"));
                _frames.Writer.TryComplete();
                _closed.TrySetResult(closeReason);
            }
        }

        private void Dispatch(string frame)
        {
            if (!_generation.Task.IsCompleted && frame.StartsWith("{\"type\":\"stream\",", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(frame);
                if (doc.RootElement.TryGetProperty("session", out var session) && session.GetString() is { Length: > 0 } id)
                {
                    _sessionId = id;
                }

                _generation.TrySetResult(doc.RootElement.GetProperty("generation").GetInt32().ToString());
                return;
            }

            _frames.Writer.TryWrite(frame);
        }

        public async ValueTask DisposeAsync()
        {
            await _pumpCts.CancelAsync();
            _response.Dispose();
            try
            {
                await _pump;
            }
            catch
            {
                // The pump swallows its own endings; nothing here is the test's concern.
            }

            _pumpCts.Dispose();
        }
    }
}
