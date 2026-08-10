// rask-rewrite: keep the factory — this benchmark IS the factory-versus-entry comparison. Converting
// its factory arm would leave two identical hosts and a measured difference of zero.
// tools/RaskBuilderRewrite skips any file carrying this marker.

using BenchmarkDotNet.Attributes;
using Rask.Core;
using C = Rask.Core.Components.Generated;

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
// than per call site, and it just grew a second, more expensive form: a property whose setter has a
// BODY is now assigned unconditionally instead of being skipped when it already reads as its default
// (Router.Routes derives the routing table from being handed a null, so skipping the write rendered an
// empty page). Five props on the shared Element/Component surface take that form — Draggable, Role,
// TabIndex, Aria, Ref — so every element in every entry-built tree pays for it, and "it is only a
// field write" is a claim, not a measurement.
[MemoryDiagnoser]
public class BuilderSurfaceBenchmarks
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
                    C.Span(Class: "label")[$"Item {i}"]
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
