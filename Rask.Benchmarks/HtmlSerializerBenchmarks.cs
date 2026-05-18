using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Components;
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
    private Component _tiny = null!;
    private Component _medium = null!;
    private Component _large = null!;

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
        var rows = new List<Child>(capacity: rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;")[
                C.Span(Class: "label")[$"Item {i}"],
                C.A(Href: $"/item/{i}", Target: "_blank", Rel: "noopener", Class: "lnk")[$"open {i}"],
                C.Img(Src: $"/img/{i}.png", Alt: $"item {i}", Width: 32, Height: 32, Loading: "lazy"),
                C.Input(Type: "text", Name: $"f{i}", Value: $"v{i}", Placeholder: "edit", MaxLength: 64)
            ]);
        }

        return C.Div(Class: "container", Id: "root")[
            C.Div(Class: "header")[
                C.Span(Class: "title")["Benchmark Tree"],
                C.Button(Type: "button", Class: "btn", Disabled: false)["Click"]
            ],
            C.Div(Class: "body")[rows]
        ];
    }
}
