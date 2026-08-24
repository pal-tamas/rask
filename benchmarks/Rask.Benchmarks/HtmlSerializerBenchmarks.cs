using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.ScopedAssets;

namespace Rask.Benchmarks;

// Renders representative trees to HTML. Captures the per-render allocation cost of
// the attribute writer rewrite (BuildAttributes IEnumerable<KVP> yield → direct
// WriteAttributes(StringBuilder)) and — after PR2 — the StringBuilder pooling on
// Component.ToHtml. Three sizes so PR2's allocation delta is visible across small,
// medium, and large render trees.
[MemoryDiagnoser]
public partial class HtmlSerializerBenchmarks : global::Rask.Core.RaskMarkup
{
    private Component _large = null!;
    private Component _medium = null!;
    private Component _scopedCss = null!;
    private Component _textHeavy = null!;
    private Component _tiny = null!;

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
        ScopedAssetRegistry.RegisterCss(typeof(ScopedRowA), ".row { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(ScopedRowB), ".row { color: green; }");
        ScopedAssetRegistry.RegisterCss(typeof(ScopedRowC), ".row { color: blue; }");
        ScopedAssetRegistry.RegisterCss(typeof(ScopedRowD), ".row { color: yellow; }");

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
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(Div.Class("row").Id($"r{i}").Style("display:flex;gap:8px;").Key(i)[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Target("_blank").Rel("noopener").Class("lnk")[$"open {i}"],
                Img.Src($"/img/{i}.png").Alt($"item {i}").Width(32).Height(32).Loading("lazy"),
                Input.Value($"v{i}").Type(InputType.Text).Name($"f{i}").Placeholder("edit").MaxLength(64)
            ]);
        }

        return Div.Class("container").Id("root")[
            Div.Class("header")[
                Span.Class("title")["Benchmark Tree"],
                Button.Type("button").Class("btn").Disabled(false)["Click"]
            ],
            Div.Class("body")[rows]
        ];
    }

    private static Component BuildScopedCssTree(int rowCount)
    {
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add((i & 3) switch
            {
                0 => ScopedRowA.Index(i),
                1 => ScopedRowB.Index(i),
                2 => ScopedRowC.Index(i),
                _ => ScopedRowD.Index(i)
            });
        }

        return [
            Doctype,
            Html[
                Body[Div.Class("list")[rows]]
            ]
        ];
    }

    private static Component BuildTextHeavyTree(int rowCount)
    {
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            // All values are encoding-free ASCII so the HtmlEncoder.Encode call returns
            // a string allocation that's literally identical to the input. The proposed
            // fast-path (scan-for-special-chars, append verbatim) elides that allocation.
            rows.Add(Div.Class("row").Id($"r{i}").Key(i)[
                Span.Class("label")[$"item {i}"],
                Span.Class("value")[$"value {i}"],
                Span.Class("meta")[$"meta {i}"],
                Span.Class("tail")[$"tail {i}"]
            ]);
        }

        return Div.Class("container").Id("root")[
            Div.Class("body")[rows]
        ];
    }
}

// Four distinct user-component subclasses so the scoped-css bench exercises four
// distinct ScopedCssRegistry entries. The cycle in BuildScopedCssTree avoids the
// degenerate "all-same-type" cache-hit case that would mask the lookup cost.
#pragma warning disable RASK014
public sealed partial class ScopedRowA : Component
{
    public int Index { get; set; }
    protected override Component? Render() => Div.Class("row").Id($"a{Index}")[Span[$"a {Index}"]];
}

public sealed partial class ScopedRowB : Component
{
    public int Index { get; set; }
    protected override Component? Render() => Div.Class("row").Id($"b{Index}")[Span[$"b {Index}"]];
}

public sealed partial class ScopedRowC : Component
{
    public int Index { get; set; }
    protected override Component? Render() => Div.Class("row").Id($"c{Index}")[Span[$"c {Index}"]];
}

public sealed partial class ScopedRowD : Component
{
    public int Index { get; set; }
    protected override Component? Render() => Div.Class("row").Id($"d{Index}")[Span[$"d {Index}"]];
}
#pragma warning restore RASK014
