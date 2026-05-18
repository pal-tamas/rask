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
