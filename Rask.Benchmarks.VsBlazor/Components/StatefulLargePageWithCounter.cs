using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

// Stateful counterpart to LargePageWithCounter.BuildRask(int). The rebuild-each-time
// factory was producing 621 KB / iter on CounterOnLargePage — 200 fresh Div+Span+A
// components plus a fresh 200-element List<Child> per call. That mass dwarfed the
// diff codec's own pooled-buffer allocations and made the per-iteration MemoryDiagnoser
// number measure tree construction, not the diff path.
//
// This component caches the 200 rows once on first Render() and rebuilds only the
// counter cell (the one node that actually mutates). Subsequent renders walk a
// stable tree of cached row Divs — exactly the shape a production LiveSession sees
// when a stateful page component re-renders with one state field changed.
//
// Tick() bumps _counter and flips _stateDirty via StateHasChanged(). RenderHandle is
// null in the bench (no LiveSession), so StateHasChanged() is a no-op past the
// dirty-flag flip; the harness drives serialization explicitly. RenderForLive sees
// _stateDirty=true and re-runs Render(), producing a fresh outer wrapper around the
// cached rows with the new counter text spliced in.
#pragma warning disable RASK014
public sealed class StatefulLargePageWithCounter : Component
#pragma warning restore RASK014
{
    public const int LargePageRowCount = 200;

    private List<Child>? _rows;
    private int _counter;

    public int Counter => _counter;

    public void Tick()
    {
        _counter++;
        StateHasChanged();
    }

    protected override RenderResult Render()
    {
        if (_rows is null)
        {
            _rows = new List<Child>(LargePageRowCount);
            for (var i = 0; i < LargePageRowCount; i++)
            {
                _rows.Add(C.Div(Class: "row", Id: $"r{i}")[
                    C.Span(Class: "label")[$"Item {i}"],
                    C.A($"/item/{i}", Class: "lnk")[$"open {i}"]
                ]);
            }
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "counter", Id: "counter")[
                            C.Span(Class: "value")[_counter.ToString()]
                        ],
                        C.Div(Class: "body")[_rows]
                    ]
                ]
            ]
        ];
    }
}
