using BenchmarkDotNet.Attributes;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// Renders trees where every leaf carries Aria (a dictionary bag), Role, and TabIndex. Isolates
// the a11y attribute path in Element.WriteAttributes: foreach over the IReadOnlyDictionary
// interface boxed an enumerator per Aria-bearing element, and TabIndex.ToString() allocated a
// string per element, on every render. The Dictionary struct-enumerator fast path and the
// integer AppendAttr overload remove both.
[MemoryDiagnoser]
public class AccessibilityAttributesBenchmarks
{
    private Component _large = null!;
    private Component _medium = null!;
    private Component _small = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = BuildTree(10);
        _medium = BuildTree(100);
        _large = BuildTree(1000);
    }

    [Benchmark]
    public string RenderSmall() => _small.ToHtml();

    [Benchmark]
    public string RenderMedium() => _medium.ToHtml();

    [Benchmark]
    public string RenderLarge() => _large.ToHtml();

    private static Component BuildTree(int rowCount)
    {
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var aria = new Dictionary<string, string?>
            {
                ["label"] = $"row {i}",
                ["selected"] = "false"
            };
            rows.Add(C.Div(Class: "row", Role: "row", TabIndex: i, Aria: aria, Key: $"k{i}")[
                C.Span(Role: "gridcell", Aria: new Dictionary<string, string?> { ["hidden"] = "false" })[$"Item {i}"]
            ]);
        }

        return C.Div(Class: "container", Role: "grid", TabIndex: 0)[rows];
    }
}
