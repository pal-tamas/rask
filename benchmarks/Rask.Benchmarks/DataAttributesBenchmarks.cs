using BenchmarkDotNet.Attributes;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// Renders trees where every leaf carries a handful of data-* attributes. Isolates the
// per-attribute allocation cost in Element.WriteAttributes — pre-change every data-*
// pair allocated an intermediate "data-{key}" string via `"data-" + kv.Key`; the new
// AppendAttr(sb, prefix, suffix, value) overload writes both parts straight into the
// pooled StringBuilder.
[MemoryDiagnoser]
public class DataAttributesBenchmarks
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
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var data = new Dictionary<string, string?>
            {
                ["index"] = i.ToString(),
                ["state"] = "idle",
                ["test-id"] = $"row-{i}"
            };
            rows.Add(C.Div(Class: "row", Data: data, Key: $"k{i}")[
                C.Span(Data: new Dictionary<string, string?> { ["label"] = "x" })[$"Item {i}"]
            ]);
        }

        return C.Div(Class: "container", Data: new Dictionary<string, string?> { ["root"] = "1" })[rows];
    }
}
