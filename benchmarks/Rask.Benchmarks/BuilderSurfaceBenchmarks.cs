using BenchmarkDotNet.Attributes;
using Rask.Core;

#pragma warning disable RASK014 // a benchmark owns the root it re-renders; there is no parent to build it

namespace Rask.Benchmarks;

// A 50-row tree re-rendered as a live root, which is the only shape where the entries do their real
// work: identity comes from GetOrCreate, and the props a chain did NOT name are put back by the
// deferred reset at the end of the parent's Render().
//
// The measured run is a STEADY-STATE re-render (the host is rendered once in setup), because that is
// where the reset lives. A first render has nothing to put back; it is the second and every one after
// it where the chain writes what it names and resets the rest.
//
// This exists because the reset is the one part of the chain that costs per render rather than per call
// site. It used to carry a second arm — the identical tree through the generated factory — as the A/B
// for that cost. That arm is gone with the factory, so the numbers it established are recorded here
// instead: allocation was at parity (19.7 KB on both arms, ratio 1.00) and the chain ran roughly 18%
// behind the factory on wall clock, error bars nowhere near touching.
//
// What that 18% was NOT: the reset's second, more expensive form — a property whose setter has a BODY,
// assigned unconditionally rather than skipped when it already reads as its default. Narrowing the
// unconditional path to Router.Routes alone moved the ratio 1.18 -> 1.17; the other four props on the
// shared surface (Draggable, Role, TabIndex, Aria, Ref) are pure forwarding setters and cost nothing
// here. #683 ruled the comparer out too — the JIT devirtualizes it — and landed on the cost being
// structural: the per-step bookkeeping every setter does (Track + Written, 150 calls a frame at this
// size) and the reset's own shape, a mask test per prop per component per render plus a
// delegate-indirected reset call per entry, whether or not the chain named anything.
//
// With one arm there is no ratio left to read; what this now guards is the absolute number moving.
[MemoryDiagnoser]
public partial class BuilderSurfaceBenchmarks
{
    private const int Rows = 50;

    private BuilderRowsHost _entry = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entry = new BuilderRowsHost();
        _entry.RenderAsLiveRoot();
    }

    [Benchmark]
    public string Entry() => _entry.RenderAsLiveRoot();

    internal sealed partial class BuilderRowsHost : Component
    {
        protected override Component? Render()
        {
            var rows = new List<Component>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(Div.Class("row").Id($"r{i}").Key(i)[
                    Span.Class("label")[$"Item {i}"]
                ]);
            }

            return Div.Class("container")[rows];
        }
    }
}
