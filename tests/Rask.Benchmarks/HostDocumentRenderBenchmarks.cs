using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server;

namespace Rask.Benchmarks;

// A session's render-and-send for an app on RaskApp or the WASM host, whose root carries the host's document
// defaults — and since toasts are built in, the host's toast outlet after the app. The two benches differ only by
// that outlet, so their delta is its whole per-frame cost on a page that is showing no toast.
[MemoryDiagnoser]
public class HostDocumentRenderBenchmarks
{
    private ServiceProvider _withoutToasts = null!;
    private ServiceProvider _withToasts = null!;
    private SessionHarness.SessionHandle _plain;
    private SessionHarness.SessionHandle _toasting;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _withoutToasts = SessionHarness.NewHost();
        _withToasts = SessionHarness.NewHost();
        _plain = SessionHarness.Create(
            _withoutToasts.GetRequiredService<LiveSessionStore>(), rows: 20, connected: true, Defaults(toasts: false));
        _toasting = SessionHarness.Create(
            _withToasts.GetRequiredService<LiveSessionStore>(), rows: 20, connected: true, Defaults(toasts: true));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        SessionHarness.Remove(_withoutToasts.GetRequiredService<LiveSessionStore>(), _plain);
        SessionHarness.Remove(_withToasts.GetRequiredService<LiveSessionStore>(), _toasting);
        _withoutToasts.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _withToasts.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public void RenderAndSendWithoutToasts() => SessionHarness.Drive(_plain.Session, _plain.App, 1);

    [Benchmark]
    public void RenderAndSendWithBuiltInToasts() => SessionHarness.Drive(_toasting.Session, _toasting.App, 1);

    private static RaskDocumentDefaults Defaults(bool toasts) =>
        new(null, null, null, toasts ? static (_, _) => Markup.Div["toast"] : null, TimeSpan.FromSeconds(5));
}
