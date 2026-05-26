using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.ScopedCss;
using B = Rask.Benchmarks.Components;
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
    private Component _scopedCss = null!;
    private Component _textHeavy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tiny = BuildTree(5);
        _medium = BuildTree(100);
        _large = BuildTree(1000);

        // Register CSS for each user-component subclass before building the scoped tree
        // so every PushScope lookup hits the registered-entry branch (lock + dict hit +
        // ScopePopper allocation + data-{scopeId} stamping on every body element).
        // The cost we want to measure is the steady-state per-render lookup, not the
        // first-time registration — the registration is one-shot.
        ScopedCssRegistry.RegisterType(typeof(ScopedRowA), ".row { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(ScopedRowB), ".row { color: green; }");
        ScopedCssRegistry.RegisterType(typeof(ScopedRowC), ".row { color: blue; }");
        ScopedCssRegistry.RegisterType(typeof(ScopedRowD), ".row { color: yellow; }");

        _scopedCss = BuildScopedCssTree(200);
        _textHeavy = BuildTextHeavyTree(200);
    }

    [Benchmark]
    public string RenderTiny() => _tiny.ToHtml();

    [Benchmark]
    public string RenderMedium() => _medium.ToHtml();

    [Benchmark]
    public string RenderLarge() => _large.ToHtml();

    // 200-row tree where each row is a registered-scope user-component subclass cycled
    // across four distinct types. Exercises ScopedCssRegistry.TryRegister (lock + dict
    // lookup per user-component render), ScopePopper allocation, and data-{scopeId}
    // stamping on every rendered body element. This is the bench item 3 in the plan
    // (ScopedCssRegistry per-type read cache) should improve.
    [Benchmark]
    public string RenderTreeWithScopedCss() => _scopedCss.RenderAsLiveRoot();

    // 200-row tree with one Text node per row holding characters that DON'T need
    // encoding (typical attribute and label values). Isolates the
    // HtmlEncoder.Default.Encode allocation path (HtmlSerializer.cs:29 Text.Render
    // and Component.cs AppendAttr value path) — bench item 1 in the plan introduces
    // a "no special chars → Append verbatim" fast-path that should drop gen0 here.
    [Benchmark]
    public string RenderTextHeavyTree() => _textHeavy.ToHtml();

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

    private static Component BuildScopedCssTree(int rowCount)
    {
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add((i & 3) switch
            {
                0 => B.ScopedRowA(i),
                1 => B.ScopedRowB(i),
                2 => B.ScopedRowC(i),
                _ => B.ScopedRowD(i)
            });
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[C.Div(Class: "list")[rows]]
            ]
        ];
    }

    private static Component BuildTextHeavyTree(int rowCount)
    {
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            // All values are encoding-free ASCII so the HtmlEncoder.Encode call returns
            // a string allocation that's literally identical to the input. The proposed
            // fast-path (scan-for-special-chars, append verbatim) elides that allocation.
            rows.Add(C.Div(Class: "row", Id: $"r{i}")[
                C.Span(Class: "label")[$"item {i}"],
                C.Span(Class: "value")[$"value {i}"],
                C.Span(Class: "meta")[$"meta {i}"],
                C.Span(Class: "tail")[$"tail {i}"]
            ]);
        }

        return C.Div(Class: "container", Id: "root")[
            C.Div(Class: "body")[rows]
        ];
    }
}

// Four distinct user-component subclasses so the scoped-css bench exercises four
// distinct ScopedCssRegistry entries. The cycle in BuildScopedCssTree avoids the
// degenerate "all-same-type" cache-hit case that would mask the lookup cost.
#pragma warning disable RASK014
public sealed class ScopedRowA : Component
{
    public int Index { get; set; }
    protected override Component Render() => C.Div(Class: "row", Id: $"a{Index}")[C.Span()[$"a {Index}"]];
}

public sealed class ScopedRowB : Component
{
    public int Index { get; set; }
    protected override Component Render() => C.Div(Class: "row", Id: $"b{Index}")[C.Span()[$"b {Index}"]];
}

public sealed class ScopedRowC : Component
{
    public int Index { get; set; }
    protected override Component Render() => C.Div(Class: "row", Id: $"c{Index}")[C.Span()[$"c {Index}"]];
}

public sealed class ScopedRowD : Component
{
    public int Index { get; set; }
    protected override Component Render() => C.Div(Class: "row", Id: $"d{Index}")[C.Span()[$"d {Index}"]];
}
#pragma warning restore RASK014
