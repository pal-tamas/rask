using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A BOUND form control, rendered end to end — the shape the suite had no view of (#802).
//
// ExpressionAccessorBenchmarks already covers ExpressionAccessor.Parse across the Bind shapes, and
// LiveDiffPayload_InputTypingBurstBenchmarks reads like form coverage. Neither measures this: Parse is
// one piece of the per-render cost, and the diff benchmark's inputs are Input.Value(...), i.e.
// CONTROLLED, so the accessor, the auto-created EditContext, the two bound handlers and the validator
// registration are all off its path.
//
// Three arms, mirroring the absolute pins in BuilderEntryAllocationPinTests so the benchmark suite and
// the unit gate describe the same decomposition and can be read against each other:
//
//   Controlled     the same form with Input.Value — the floor, and the honest comparison
//   BoundHoisted   the bind expression built ONCE in setup, so this moves only when Rask's bind path does
//   Bound          the expression built at the CALL SITE, which is what a user actually writes
//
// Bound minus BoundHoisted is the C# compiler constructing an Expression<Func<T>> per render. It is not
// Rask code and nothing here can make it cheaper — measured at 46% of a single bound control's cost in
// #793 — so a regression hunt starts at BoundHoisted, not at Bound.
[MemoryDiagnoser]
public partial class BoundControlRenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private readonly TypingModel _model = new();

    private Expression<Func<string>> _boundA = null!;
    private Expression<Func<string>> _boundB = null!;
    private Expression<Func<string>> _boundC = null!;

    [GlobalSetup]
    public void Setup()
    {
        _boundA = () => _model.A;
        _boundB = () => _model.B;
        _boundC = () => _model.C;
    }

    [Benchmark(Baseline = true)]
    public string Controlled() =>
        Form.Model(_model)[
            Label["Field A"],
            Input.Value(_model.A).Type(InputType.Text).Name("a"),
            Label["Field B"],
            Input.Value(_model.B).Type(InputType.Text).Name("b"),
            Label["Field C"],
            Input.Value(_model.C).Type(InputType.Text).Name("c"),
            Button.Type("submit")["Save"]
        ].RenderAsLiveRoot();

    [Benchmark]
    public string BoundHoisted() =>
        Form.Model(_model)[
            Label["Field A"],
            Input.Bind(_boundA).Type(InputType.Text),
            Label["Field B"],
            Input.Bind(_boundB).Type(InputType.Text),
            Label["Field C"],
            Input.Bind(_boundC).Type(InputType.Text),
            Button.Type("submit")["Save"]
        ].RenderAsLiveRoot();

    [Benchmark]
    public string Bound() =>
        Form.Model(_model)[
            Label["Field A"],
            Input.Bind(() => _model.A).Type(InputType.Text),
            Label["Field B"],
            Input.Bind(() => _model.B).Type(InputType.Text),
            Label["Field C"],
            Input.Bind(() => _model.C).Type(InputType.Text),
            Button.Type("submit")["Save"]
        ].RenderAsLiveRoot();

    // Settable properties: Bind needs a terminal property it can write back through.
    private sealed class TypingModel
    {
        public string A { get; set; } = "abc";

        public string B { get; set; } = "field B initial";

        public string C { get; set; } = "field C initial";
    }
}
