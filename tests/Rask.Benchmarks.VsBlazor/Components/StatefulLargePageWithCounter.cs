using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

// Stateful counterpart to LargePageWithCounter.BuildRask(int). The rebuild-each-time
// factory was producing 621 KB / iter on CounterOnLargePage — 200 fresh Div+Span+A
// components plus a fresh 200-element List<Component> per call. That mass dwarfed the
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
public sealed partial class StatefulLargePageWithCounter : Component
#pragma warning restore RASK014
{
    public const int LargePageRowCount = 200;

    private List<Component>? _rows;

    public int Counter { get; private set; }

    public void Tick()
    {
        Counter++;
        StateHasChanged();
    }

    protected override Component? Render()
    {
        if (_rows is null)
        {
            _rows = new List<Component>(LargePageRowCount);
            for (var i = 0; i < LargePageRowCount; i++)
            {
                _rows.Add(Div.Class("line").Id($"r{i}")[
                    Span.Class("label")[$"Item {i}"],
                    A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
                ]);
            }
        }

        return Div.Class("wrap").Id("root")[
            Div.Class("counter").Id("counter")[
                Span.Class("value")[Counter.ToString()]
            ],
            Div.Class("body")[_rows]
        ];
    }
}

// Variant where the mutating text lives deep inside the row list. Cached rows
// surround the one mutating cell; on each Tick the cell's text span is rebuilt
// in place and the cached list is left otherwise untouched. Mirrors the
// LargePageWithCounter.BuildRaskWithDeepTextCell() factory but reuses 199 of
// 200 row instances across iterations.
#pragma warning disable RASK014
public sealed partial class StatefulLargePageWithDeepTextCell : Component
#pragma warning restore RASK014
{
    public const int LargePageRowCount = 200;
    private const int MutatingIndex = LargePageRowCount / 2;

    private Component[]? _rowsByIndex;
    private List<Component>? _scratch;

    public int Counter { get; private set; }

    public void Tick()
    {
        Counter++;
        StateHasChanged();
    }

    protected override Component? Render()
    {
        if (_rowsByIndex is null)
        {
            _rowsByIndex = new Component[LargePageRowCount];
            for (var i = 0; i < LargePageRowCount; i++)
            {
                if (i == MutatingIndex)
                {
                    continue; // built fresh each render
                }

                _rowsByIndex[i] = Div.Class("line").Id($"r{i}")[
                    Span.Class("label")[$"Item {i}"],
                    A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
                ];
            }
        }

        _rowsByIndex[MutatingIndex] = Div.Class("line").Id($"r{MutatingIndex}")[
            Span.Class("label")[$"ticker {Counter}"],
            A.Href($"/item/{MutatingIndex}").Class("lnk")[$"open {MutatingIndex}"]
        ];

        _scratch ??= new List<Component>(LargePageRowCount);
        _scratch.Clear();
        for (var i = 0; i < LargePageRowCount; i++)
        {
            _scratch.Add(_rowsByIndex[i]);
        }

        return Div.Class("body")[_scratch];
    }
}
