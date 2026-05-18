using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Components;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks;

// Renders a representative ~100-element tree to HTML. Captures the per-render allocation
// cost of the attribute writer rewrite (BuildAttributes IEnumerable<KVP> yield → direct
// WriteAttributes(StringBuilder)). All construction goes through generated factories so
// RASK014 is satisfied.
[MemoryDiagnoser]
public class HtmlSerializerBenchmarks
{
    private Component _tree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tree = BuildTree();
    }

    [Benchmark]
    public string Render() => _tree.ToHtml();

    private static Component BuildTree()
    {
        var rows = new List<Child>(capacity: 20);
        for (var i = 0; i < 20; i++)
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
