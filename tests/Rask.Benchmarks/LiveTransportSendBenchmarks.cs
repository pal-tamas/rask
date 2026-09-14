using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using Rask.Benchmarks.Infrastructure;
using Rask.Server.Transport;

namespace Rask.Benchmarks;

/// <summary>
///     One frame through each transport: the WebSocket's pass-through, and the HTTP fallback's event-stream framing.
/// </summary>
/// <remarks>
///     The fallback writes frames by hand into the response's <see cref="PipeWriter" /> rather than through
///     <c>TypedResults.ServerSentEvents</c>, on the claim that a frame is already UTF-8 bytes and needs no second
///     serialisation. This is that claim measured: the event-stream write should allocate nothing per frame once
///     the pipe's segments are warm, whatever the frame's size.
/// </remarks>
[MemoryDiagnoser]
public class LiveTransportSendBenchmarks
{
    private byte[] _frame = null!;
    private WebSocketTransport _webSocket = null!;
    private SseTransport _sse = null!;

    /// <summary>A small diff, and a full-page frame.</summary>
    [Params(256, 16_384)]
    public int FrameBytes { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _frame = new byte[FrameBytes];
        Array.Fill(_frame, (byte)'x');
        _webSocket = new WebSocketTransport(new NullWebSocket());
        _sse = new SseTransport(PipeWriter.Create(Stream.Null), generation: 1);
    }

    [Benchmark(Baseline = true)]
    public void WebSocket() => _webSocket.SendAsync(_frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public void ServerSentEvents() => _sse.SendAsync(_frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();
}
