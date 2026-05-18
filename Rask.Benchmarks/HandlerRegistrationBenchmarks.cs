using BenchmarkDotNet.Attributes;
using Rask.Core;
using B = Rask.Benchmarks.Components;

namespace Rask.Benchmarks;

// Stresses Component.RegisterHandler in isolation — the interning array (_smallHandlerIds
// covers h0..h255) is the win to measure. Past 256 handlers, the path falls back to a
// "h" + n concat which allocates a string per call. Two benchmarks: well-under and
// well-over the intern threshold, so a regression in either branch shows up.
[MemoryDiagnoser]
public class HandlerRegistrationBenchmarks
{
    private RegHost _host = null!;
    private Action _action = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _action = () => { };
    }

    // Fresh host per iteration: avoids needing private-field reset access on Component
    // and keeps each [Benchmark] starting from _nextHandlerId == 0. The allocation is
    // outside the measured region. Construction goes through the generated factory so
    // RASK014 is satisfied.
    [IterationSetup]
    public void IterationSetup() => _host = (RegHost)B.RegHost();

    [Benchmark]
    public string Register200()
    {
        // 200 fits well inside the intern table — represents a typical complex page.
        // No string-concat allocations on this path; the dictionary insert dominates.
        string? last = null;
        for (var i = 0; i < 200; i++)
        {
            last = _host.RegisterHandler(_action);
        }
        return last!;
    }

    [Benchmark]
    public string Register1000()
    {
        // 1000 exceeds the 256-slot intern table; the 744 over-the-line registrations
        // hit the "h" + n concat path. Captures both phases in one number.
        string? last = null;
        for (var i = 0; i < 1000; i++)
        {
            last = _host.RegisterHandler(_action);
        }
        return last!;
    }
}

// RegHost exists because Component.RegisterHandler is internal; this subclass re-exposes
// it as a public method so the bench can call it directly without going through the
// full RenderAsLiveRoot path.
internal sealed class RegHost : Component
{
    public new string RegisterHandler(Delegate handler) => base.RegisterHandler(handler);
}
