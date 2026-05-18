using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Components;
using C = Rask.Core.Components.Components;
using B = Rask.Benchmarks.Components;

namespace Rask.Benchmarks;

// Drives the full RenderAsLiveRoot path — the function LiveSession.RenderAndSendAsync
// chains every WS frame. Unlike RenderRoundTripBenchmarks (which calls ToHtml() directly),
// this exercises the live-render reconciliation: _children swap, _handlers dict, alive-set
// HashSets, parent map, handler-id generation. The tree mixes a user Component subclass
// (RowItem) into the framework-tag tree because only user components go through
// RenderForLive and hit the _children dict path; framework tags are walked structurally
// by HtmlSerializer.
//
// Two benchmarks: a single render (mostly first-render cost) and a 10x loop (amortises
// first-render warmup, so the steady-state allocation per render dominates). The 10x is
// the one to watch when comparing before/after.
[MemoryDiagnoser]
public class LiveRenderRoundTripBenchmarks
{
    private Component _tree = null!;

    [IterationSetup]
    public void IterationSetup() => _tree = BuildTree();

    [Benchmark]
    public string RenderOnce() => _tree.RenderAsLiveRoot();

    [Benchmark]
    public string RenderTenTimes()
    {
        string? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = _tree.RenderAsLiveRoot();
        }
        return last!;
    }

    // 100-row list with data-rask-key emission per row. Mirrors the Virtualize / sortable
    // table pattern: every render shuffles the row order so the keyed-morph branch (in
    // rask-morph.js) would do an O(k) reorder client-side instead of replacing every node.
    // The C# side measures the server-render allocation cost of producing the keyed HTML;
    // actual reorder-vs-replace cost lives in the browser and is harness-measured.
    private int _reorderSeed;

    [Benchmark]
    public string RenderKeyedList100_ShuffledEachIteration()
    {
        _reorderSeed = (_reorderSeed + 1) & 0x7fffffff;
        var tree = BuildKeyedListTree(100, _reorderSeed);
        return tree.RenderAsLiveRoot();
    }

    private static Component BuildTree()
    {
        var rows = new List<Child>(capacity: 20);
        for (var i = 0; i < 20; i++)
        {
            rows.Add(B.RowItem(Index: i));
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "header")[C.Span()["Live Bench"]],
                        C.Div(Class: "body")[rows]
                    ]
                ]
            ]
        ];
    }

    private static Component BuildKeyedListTree(int count, int seed)
    {
        // Deterministic shuffle so the benchmark output is reproducible while still
        // exercising the morph reorder branch on every iteration.
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        var rnd = new Random(seed);
        for (var i = count - 1; i > 0; i--)
        {
            var j = rnd.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var rows = new List<Child>(capacity: count);
        for (var i = 0; i < count; i++)
        {
            var idx = order[i];
            rows.Add(C.Div(
                Class: "row",
                Data: new Dictionary<string, string?> { ["rask-key"] = idx.ToString() })[
                C.Span()[$"Item {idx}"]
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "list")[rows]]]
        ];
    }
}

// User Component subclass exists so HtmlSerializer routes the row through RenderForLive
// (the only path that swaps _children) and registers a handler (the only path that
// touches _handlers + _nextHandlerId). 20 rows × 1 handler each = 20 RegisterHandler
// calls per render — enough to see the handler-id intern cut and the dictionary reuse.
public sealed class RowItem : Component
{
    public int Index { get; set; }

    protected override Component Render() =>
        C.Div(Class: "row", Id: $"r{Index}")[
            C.Span(Class: "label")[$"Item {Index}"],
            C.A(Href: $"/item/{Index}", Class: "lnk")[$"open {Index}"],
            C.Button(Type: "button", OnClick: () => { })["go"]
        ];
}
