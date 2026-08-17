// rask-rewrite: keep the factory — this benchmark IS the factory-versus-entry comparison. Converting
// its factory arm would leave two identical hosts and a measured difference of zero.
// tools/RaskBuilderRewrite skips any file carrying this marker.

using BenchmarkDotNet.Attributes;
using Rask.Core;
using C = Rask.Core.Components.Generated;

using CH = Rask.Html.Components.Generated;
#pragma warning disable RASK014 // a benchmark owns the root it re-renders; there is no parent to build it

namespace Rask.Benchmarks;

// The same 50-row tree built two ways — through the generated factory, and through the builder
// entries that are meant to replace it. Both hosts are rendered as a live root, which is the only
// shape where the entries do their real work: identity comes from GetOrCreate, and the props a chain
// did NOT name are put back by the deferred reset at the end of the parent's Render().
//
// The measured runs are STEADY-STATE re-renders (both hosts are rendered once in setup), because that
// is where the difference between the two surfaces lives. A first render has nothing to put back; it
// is the second and every one after it where the factory re-assigns every parameter and the chain
// instead writes what it names and resets the rest.
//
// This exists because the reset is the one part of the builder surface that costs per render rather
// than per call site. It reports allocation parity (19.7 KB on both arms, Alloc Ratio 1.00) and the
// chain roughly 18% behind on wall clock, with error bars nowhere near touching.
//
// What that 18% is NOT: this comment used to name the cause as the reset's second, more expensive
// form — a property whose setter has a BODY assigned unconditionally rather than skipped when it
// already reads as its default (Router.Routes derives the routing table from being handed a null, so
// skipping the write rendered an empty page), with five props on the shared surface taking that form
// (Draggable, Role, TabIndex, Aria, Ref). That was the prediction the comment carried before anyone
// measured it, and measuring it DISPROVED it: narrowing the unconditional path to Router.Routes alone
// moves the ratio 1.18 -> 1.17. The other four are pure forwarding setters and cost nothing here.
//
// What is still open: the per-step bookkeeping every setter does (Track + Written, 150 calls a frame
// at this size) and the reset's own shape — a mask test per prop per component per render, plus a
// delegate-indirected reset call per entry, whether or not the chain named anything. Tracked in #683,
// which must be settled BEFORE the generated factory is dropped: that removes the Factory arm below
// and with it the only A/B this number can be measured against.
[MemoryDiagnoser]
public partial class BuilderSurfaceBenchmarks
{
    private const int Rows = 50;

    private BuilderRowsHost _entry = null!;
    private FactoryRowsHost _factory = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entry = new BuilderRowsHost();
        _factory = new FactoryRowsHost();
        _entry.RenderAsLiveRoot();
        _factory.RenderAsLiveRoot();
    }

    [Benchmark(Baseline = true)]
    public string Factory() => _factory.RenderAsLiveRoot();

    [Benchmark]
    public string Entry() => _entry.RenderAsLiveRoot();

    internal sealed partial class FactoryRowsHost : Component
    {
        protected override Component? Render()
        {
            var rows = new List<Component>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(C.Div(Class: "row", Id: $"r{i}", Key: i)[
                    CH.Span(Class: "label")[$"Item {i}"]
                ]);
            }

            return C.Div(Class: "container")[rows];
        }
    }

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
