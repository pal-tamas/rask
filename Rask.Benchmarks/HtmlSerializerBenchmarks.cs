using BenchmarkDotNet.Attributes;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks;

// Renders representative trees to HTML. Captures the per-render allocation cost of
// the attribute writer rewrite (BuildAttributes IEnumerable<KVP> yield → direct
// WriteAttributes(StringBuilder)) and — after PR2 — the StringBuilder pooling on
// Component.ToHtml. Three sizes so PR2's allocation delta is visible across small,
// medium, and large render trees.
[MemoryDiagnoser]
public class HtmlSerializerBenchmarks
{
    private Component _large = null!;
    private Component _medium = null!;
    private Component _tiny = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tiny = BuildTree(5);
        _medium = BuildTree(100);
        _large = BuildTree(1000);
    }

    [Benchmark]
    public string RenderTiny() => _tiny.ToHtml();

    [Benchmark]
    public string RenderMedium() => _medium.ToHtml();

    [Benchmark]
    public string RenderLarge() => _large.ToHtml();

    // Shape mirrors the per-row pattern used in LiveRenderRoundTripBenchmarks so the
    // two benchmark suites stay comparable. rowCount scales the body; the header is
    // constant so very small trees still exercise the same per-attribute encoder path.
    private static Component BuildTree(int rowCount)
    {
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;")[
                C.Span(Class: "label")[$"Item {i}"],
                C.A($"/item/{i}", "_blank", "noopener", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32, "lazy"),
                C.Input("text", $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return C.Div(Class: "container", Id: "root")[
            C.Div(Class: "header")[
                C.Span(Class: "title")["Benchmark Tree"],
                C.Button("button", Class: "btn", Disabled: false)["Click"]
            ],
            C.Div(Class: "body")[rows]
        ];
    }
}
