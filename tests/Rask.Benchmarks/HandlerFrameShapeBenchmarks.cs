using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// The frame-type cross-check Component.TryInvokeHandlerAsync runs before invoking a handler (#587): the
// frame declares what it carries, and a delegate that cannot be fed it is refused instead of being run.
// This is on the per-event dispatch path — every click, every coalesced keystroke, every 60 Hz scroll
// tick — so it has to cost effectively nothing and allocate nothing.
//
// Three shapes, because they exercise different amounts of the table:
//   • ClickOnParameterless — the common accept. First entry of the first row: one ValueEquals.
//   • ScrollOnScroll       — a single-entry row, the shape a high-frequency event takes.
//   • InputOnParameterless — the refusal. Misses its own row, then scans the rest to establish the type
//                            is claimed by another shape rather than simply unknown. The worst case, and
//                            the one that only happens when something already went wrong.
//   • UnknownTypeOnParameterless — a frame this build has never heard of: the full scan, then allowed.
[MemoryDiagnoser]
public class HandlerFrameShapeBenchmarks
{
    private readonly Action _parameterless = () => { };
    private readonly Action<ScrollEvent> _scroll = _ => { };
    private JsonDocument _click = null!;
    private JsonDocument _input = null!;
    private JsonDocument _scrollFrame = null!;
    private JsonDocument _unknown = null!;

    [GlobalSetup]
    public void Setup()
    {
        _click = JsonDocument.Parse("""{"id":"h17","type":"click"}"""u8.ToArray());
        _input = JsonDocument.Parse("""{"id":"h17","type":"input","value":"hello world"}"""u8.ToArray());
        _scrollFrame = JsonDocument.Parse(
            """{"id":"h17","type":"scroll","scrollTop":400,"clientHeight":800,"scrollHeight":9000}"""u8
                .ToArray());
        _unknown = JsonDocument.Parse("""{"id":"h17","type":"someFutureEvent"}"""u8.ToArray());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _click.Dispose();
        _input.Dispose();
        _scrollFrame.Dispose();
        _unknown.Dispose();
    }

    [Benchmark(Baseline = true)]
    public bool ClickOnParameterless() => HandlerFrameShape.Accepts(_click.RootElement, _parameterless);

    [Benchmark]
    public bool ScrollOnScroll() => HandlerFrameShape.Accepts(_scrollFrame.RootElement, _scroll);

    [Benchmark]
    public bool InputOnParameterless() => HandlerFrameShape.Accepts(_input.RootElement, _parameterless);

    [Benchmark]
    public bool UnknownTypeOnParameterless() => HandlerFrameShape.Accepts(_unknown.RootElement, _parameterless);
}
