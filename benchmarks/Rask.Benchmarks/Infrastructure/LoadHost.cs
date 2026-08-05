using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Server;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     A real Rask server on a loopback port, for the load report to drive with real WebSockets.
/// </summary>
/// <remarks>
///     <para>
///         Kestrel and <see cref="ClientWebSocket" />, deliberately — not the in-memory test server the
///         unit tests use. The numbers this produces are about the transport as much as the renderer:
///         frame encoding, TCP, permessage-deflate, the receive loop's buffer handling. An in-memory pipe
///         removes exactly the parts that decide whether a host can hold its clients, and would flatter
///         every number in the report.
///     </para>
///     <para>
///         Port 0 so a run cannot collide with a dev server, another worktree's E2E, or a second copy of
///         itself; the bound port is read back from the server's addresses feature.
///     </para>
/// </remarks>
internal sealed class LoadHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoadHost(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    public static async Task<LoadHost> StartAsync(int rowCount)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // The report's output is the only thing that should reach stdout.
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddRask(configureServer: o =>
        {
            // Off for the measurement, and worth being explicit about why. MaxInboundFramesPerSecond
            // (1000 by default) is a per-connection DoS backstop, not a throughput setting — no human
            // clicks a thousand times a second. A closed-loop generator does, so leaving it on means the
            // report measures the cap rather than the host: the first run of this harness reported
            // exactly 1000 events/sec per client and closed every socket, which is the limit working
            // correctly and the benchmark asking the wrong question.
            o.MaxInboundFramesPerSecond = 0;
        });
        builder.Services.AddSingleton(new LoadPageOptions(rowCount));

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.UseRask<LoadPage>();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        return new LoadHost(app, new Uri(address).Port);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     Opens one client the way a browser does: GET the page for its session id and a handler id, then
    ///     connect and say hello.
    /// </summary>
    public async Task<LoadClient> ConnectAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        var html = await http.GetStringAsync($"http://127.0.0.1:{Port}/", ct);

        var sessionId = Between(html, "data-rask-root=\"", "\"")
                        ?? throw new InvalidOperationException("the page carried no session id");
        var handlerId = Between(html, "data-rask-on-click=\"", "\"")
                        ?? throw new InvalidOperationException("the page carried no click handler");

        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/rask/ws"), ct);
        await SendAsync(ws, $"{{\"type\":\"hello\",\"session\":\"{sessionId}\"}}", ct);
        return new LoadClient(ws, handlerId);
    }

    private static string? Between(string source, string open, string close)
    {
        var start = source.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        var end = source.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : source[start..end];
    }

    internal static Task SendAsync(WebSocket ws, string json, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
}

/// <summary>One connected virtual user: its socket, and the handler it clicks.</summary>
internal sealed class LoadClient(ClientWebSocket ws, string handlerId) : IDisposable
{
    private readonly byte[] _buffer = new byte[64 * 1024];

    public void Dispose() => ws.Dispose();

    /// <summary>
    ///     Fires one event and waits for the server to acknowledge that exact one.
    /// </summary>
    /// <remarks>
    ///     The <c>seq</c> ack is what the real client uses to clear its pending indicator, and it is the
    ///     only signal that closes the round trip for an interaction whose render deduped to nothing. So it
    ///     is also the honest thing to time: click to server-has-finished-with-it, not click to first byte.
    /// </remarks>
    public async Task<bool> ClickAndAwaitAckAsync(long seq, CancellationToken ct)
    {
        await LoadHost.SendAsync(ws, $"{{\"id\":\"{handlerId}\",\"seq\":{seq}}}", ct);

        // Frames other than our ack — the render this click caused, a resume record — arrive first and in
        // any order; read past them until the round trip actually closes.
        while (true)
        {
            var frame = await ReceiveAsync(ct);
            if (frame is null)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueEquals("ack")
                && doc.RootElement.TryGetProperty("seq", out var acked)
                && acked.TryGetInt64(out var value)
                && value == seq)
            {
                return true;
            }
        }
    }

    private async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var builder = new StringBuilder();
        while (true)
        {
            var result = await ws.ReceiveAsync(_buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            builder.Append(Encoding.UTF8.GetString(_buffer, 0, result.Count));
            if (result.EndOfMessage)
            {
                return builder.ToString();
            }
        }
    }
}

/// <summary>Row count for the page under load, resolved per host rather than per component.</summary>
/// <remarks>Public because <see cref="LoadPage" /> is — the generator needs to see the root component.</remarks>
public sealed record LoadPageOptions(int RowCount);
