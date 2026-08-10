using BenchmarkDotNet.Attributes;
using Rask.Core;
using B = Rask.Benchmarks.Generated;

namespace Rask.Benchmarks;

// Stresses Component.RegisterHandler in isolation. The prebuilt id table (_smallHandlerIds covers
// h0..h1023) is the win to measure; past it, minting a slot's id falls back to a formatted string.
// Three benchmarks straddling that threshold so a regression in either branch shows up.
//
// All three measure a component's FIRST render: IterationSetup builds a fresh host, so each one pays
// the one-off cost of that component's slot table (slot 0 is a scalar; slots 1.. share one array grown
// geometrically). A re-render reuses both and mints nothing, which is why this benchmark deliberately
// does not describe steady state — LiveRenderRoundTrip's RenderTenTimes does.
//
// Numbers are cumulative rather than concurrent (an id is never reused once issued, so a stale event
// for a removed element cannot be redirected onto a live handler), which is what makes the
// past-the-table path reachable on a long-lived churny page rather than merely theoretical.
[MemoryDiagnoser]
public class HandlerRegistrationBenchmarks
{
    private Action _action = null!;
    private RegHost _host = null!;

    [GlobalSetup]
    public void GlobalSetup() => _action = () => { };

    // Fresh host per iteration: avoids needing private-field reset access on Component and keeps each
    // [Benchmark] starting from an empty slot table. The allocation is outside the measured region.
    // Construction goes through the generated factory so RASK014 is satisfied.
    [IterationSetup]
    public void IterationSetup() => _host = (RegHost)B.RegHost();

    [Benchmark]
    public string Register200() => RegisterMany(200);

    // 1000 still fits inside the prebuilt table, so this is the same branch as Register200 at 5x the
    // slot-array growth — it isolates the array from the id minting.
    [Benchmark]
    public string Register1000() => RegisterMany(1000);

    // 2000 runs ~1000 registrations past the end of the prebuilt table, so roughly half of them format
    // their id instead of reading it out. Each such string is minted once and then cached on its slot,
    // so this is the worst case for a component's first render and costs nothing on its later ones.
    [Benchmark]
    public string Register2000() => RegisterMany(2000);

    private string RegisterMany(int count)
    {
        string? last = null;
        for (var i = 0; i < count; i++)
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
