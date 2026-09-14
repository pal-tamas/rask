using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Server;

namespace Rask.Benchmarks;

/// <summary>
///     A connected session's send path, from a state change to bytes on the socket.
/// </summary>
/// <remarks>
///     Written against members that predate the transport seam — <c>AttachSocket</c>, <c>RequestRenderAsync</c>,
///     <c>SendOutOfBandAsync</c> — so the same file measures the tree before it and after it. Every frame a live
///     page sends now goes through <c>ILiveTransport</c> rather than straight to the <c>WebSocket</c>, and this is
///     where that indirection would show up if it cost anything.
/// </remarks>
[MemoryDiagnoser]
public class LiveSessionSendBenchmarks
{
    private static readonly byte[] OutOfBandFrame = """{"type":"toast","message":"saved"}"""u8.ToArray();

    private ServiceProvider _services = null!;
    private LiveSessionStore _store = null!;
    private SessionHarness.SessionHandle _handle;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _services = SessionHarness.NewHost();
        _store = _services.GetRequiredService<LiveSessionStore>();
        _handle = SessionHarness.Create(_store, rows: 20, connected: true);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        SessionHarness.Remove(_store, _handle);
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>A state change rendered, diffed and sent — what every event handler ends in.</summary>
    [Benchmark(Baseline = true)]
    public void RenderAndSend() => SessionHarness.Drive(_handle.Session, _handle.App, 1);

    /// <summary>A frame that is not a render, sent through the same guarded path.</summary>
    [Benchmark]
    public void SendOutOfBand() => _handle.Session.SendOutOfBandAsync(OutOfBandFrame).GetAwaiter().GetResult();
}
