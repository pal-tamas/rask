using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A form of kit controls rendered to HTML: 100 of each, so Allocated / 100 is the cost of one control. Each joins its
// class lists per render (a look plus whatever the call site adds), which is what UiClass and `.Class(a, b)` cost.
//
//   Input      Ui.Input.Value(text)                     — the field's wrapper and its input
//   Labelled   Ui.Input.Value(text).Label(text)         — the same inside a field, with a label
//   Checkbox   Ui.Checkbox.Value(text).Label(text)      — a control drawn from several joined parts
[MemoryDiagnoser]
public partial class UiFormControlRenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private const int Controls = 100;

    private static readonly string[] Values = [.. Enumerable.Range(0, Controls).Select(i => "Value " + i)];

    [Benchmark(Baseline = true)]
    public string Input() => Form(text => Ui.Input.Value(text));

    [Benchmark]
    public string Labelled() => Form(text => Ui.Input.Value(text).Label(text));

    [Benchmark]
    public string Checkbox() => Form(text => Ui.Checkbox.Value(text).Label(text));

    private static string Form(Func<string, Component> control) => Div[Values.Select(control)].ToHtml();
}
