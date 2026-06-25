using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// Headline metric for the Rask perf pass: bytes shipped per StateHasChanged.
//
// Today Rask ships the whole rendered body (via LivePayload.BuildPayloadUtf8WithRoot)
// on every live update — for "counter++ on a 50 KB page" that means ~50 KB on the wire
// every tick. Blazor's RenderBatch ships ~tens of bytes for the same scenario. Closing
// that structural gap is the bar that makes Rask a real competitor.
//
// These three benches lock in the baseline. After the Phase 1 diff codec lands they
// should drop by ~2 orders of magnitude for the small-change cases:
//   - CounterOnLargePage   : baseline ≈ body bytes (~30-50 KB);   target ≤ 200 bytes
//   - KeyedList100Reorder  : baseline ≈ list HTML (~10-20 KB);    target ≤ 500 bytes
//   - TextNodeUpdate       : baseline ≈ body bytes (~30-50 KB);   target ≤ 100 bytes
//
// Each iteration mutates the tree-defining state once (counter++ / swap / text flip),
// builds a fresh tree, calls RenderAsLiveRoot (the same entry point LiveSession uses),
// then runs BuildPayloadUtf8WithRoot — the exact path the server WS dispatcher uses
// to produce the payload it hands to WebSocket.SendAsync. Returning the byte count
// makes BenchmarkDotNet show the wire size as the benchmark's "Mean" column equivalent
// — the metric we're optimizing for.
[MemoryDiagnoser]
public class PayloadBytesPerUpdate
{
    private const string SessionId = "session-bench";

    // CounterOnLargePage: ~200 static rows + one counter cell. The static rows are the
    // "50 KB of unchanged DOM" payload-size-dominator that the diff codec must elide.
    private const int LargePageRowCount = 200;
    private int _counter;

    // KeyedList: 100 rows with data-rask-key. Each iteration swaps the items at
    // _swapA and _swapB and advances the indices, so the morph reorder branch is
    // exercised every render and the diff codec has a non-trivial keyed-list delta
    // to encode (two Move ops + maybe one attribute, not 100 full rows).
    private int[] _keyedOrder = null!;
    private int _swapA;
    private int _swapB;

    // TextNodeUpdate: one text node deep in a static tree changes. Tightest possible
    // diff — single UpdateText op, all surrounding bytes stable. Today's baseline still
    // re-ships the whole body because rendered HTML differs.
    private int _textCounter;

    private ArrayBufferWriter<byte> _writer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _writer = new ArrayBufferWriter<byte>(64 * 1024);

        _keyedOrder = new int[100];
        for (var i = 0; i < _keyedOrder.Length; i++)
        {
            _keyedOrder[i] = i;
        }

        _swapA = 5;
        _swapB = 95;
    }

    [Benchmark]
    public int CounterOnLargePage()
    {
        _counter++;
        var tree = BuildLargePageWithCounter(_counter);
        return BuildPayload(tree);
    }

    [Benchmark]
    public int KeyedList100Reorder()
    {
        // Swap two entries. The list shape is otherwise stable across iterations, so the
        // ideal diff is two Move ops. Today's payload re-emits the whole list.
        (_keyedOrder[_swapA], _keyedOrder[_swapB]) = (_keyedOrder[_swapB], _keyedOrder[_swapA]);
        _swapA = (_swapA + 1) % _keyedOrder.Length;
        _swapB = (_swapB + 1) % _keyedOrder.Length;

        var tree = BuildKeyedListTree(_keyedOrder);
        return BuildPayload(tree);
    }

    [Benchmark]
    public int TextNodeUpdate()
    {
        _textCounter++;
        var tree = BuildLargePageWithDeepTextCell(_textCounter);
        return BuildPayload(tree);
    }

    private int BuildPayload(Component tree)
    {
        var html = tree.RenderAsLiveRoot();
        _writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithRoot(_writer, html, SessionId, null, false);
        return _writer.WrittenCount;
    }

    private static Component BuildLargePageWithCounter(int counter)
    {
        var rows = new List<Child>(LargePageRowCount);
        for (var i = 0; i < LargePageRowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;", Key: i)[
                C.Span(Class: "label")[$"Item {i}"],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input<string>("text", $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "counter", Id: "counter")[
                            C.Span(Class: "value")[counter.ToString()]
                        ],
                        C.Div(Class: "body")[rows]
                    ]
                ]
            ]
        ];
    }

    private static Component BuildKeyedListTree(int[] order)
    {
        var rows = new List<Child>(order.Length);
        for (var i = 0; i < order.Length; i++)
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
            C.Html()[
                C.Body()[C.Div(Class: "list")[rows]]
            ]
        ];
    }

    private static Component BuildLargePageWithDeepTextCell(int counter)
    {
        // Identical large tree to BuildLargePageWithCounter but the changing value lives
        // deep inside the row list rather than at the top. Today's payload size is the
        // same regardless — the whole body is re-shipped — but the diff codec should
        // produce a single UpdateText op deep in the path.
        var rows = new List<Child>(LargePageRowCount);
        for (var i = 0; i < LargePageRowCount; i++)
        {
            var text = i == LargePageRowCount / 2 ? $"ticker {counter}" : $"Item {i}";
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;", Key: i)[
                C.Span(Class: "label")[text],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input<string>("text", $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[C.Div(Class: "body")[rows]]
            ]
        ];
    }
}
