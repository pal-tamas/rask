using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     100 keyed rows. Re-renders shuffle two entries; the diff should encode the move
///     rather than the whole list. Rask uses data-rask-key; Blazor uses SetKey().
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class KeyedList
{
    public static Component BuildRask(int[] order)
    {
        var rows = new List<Component>(order.Length);
        for (var i = 0; i < order.Length; i++)
        {
            var idx = order[i];
            rows.Add(Div
                .Class("line")
                .Data(new Dictionary<string, string?> { ["rask-key"] = idx.ToString() })[
                Span[$"Item {idx}"]
            ]);
        }

        return Div.Class("list")[rows];
    }

    // Stateful counterpart used by the live-diff payload benchmark. Mirrors the design
    // of StatefulLargePageWithCounter: cache the per-key Component wrappers once, then mutate
    // the order via a private rotation array. Each Tick swaps two slots and calls
    // StateHasChanged so the next RenderForLive emits a reordered (but otherwise
    // identical) row list — fair apples-to-apples vs Blazor's ParameterView path,
    // which also reuses its child component instances across the parameter change.
#pragma warning disable RASK014
    public sealed partial class StatefulKeyedList : Component
#pragma warning restore RASK014
    {
        private readonly Dictionary<int, Component> _rowsByKey = new();
        private int[]? _order;
        private List<Component>? _scratch;
        private int _swapA = 5;

        private int _swapB;

        // Default visible-row count for the initial order; benchmarks needing a non-zero
        // pre-seeded population set this (e.g. 100 for KeyedList100Reorder). Sparse-key
        // scenarios (Scale_KeyedAppendMiddle with inserted = N+1000) work transparently
        // because rows are lazy-allocated by key on demand into the dictionary below.
        public int InitialRowCount { get; init; } = 100;

        public int[] CurrentOrder
        {
            get
            {
                EnsureSeeded();
                return _order!;
            }
        }

        public void SwapTwo()
        {
            EnsureSeeded();
            _swapB = _swapB == 0 ? _order!.Length - 5 : _swapB;
            (_order![_swapA], _order[_swapB]) = (_order[_swapB], _order[_swapA]);
            _swapA = (_swapA + 1) % _order.Length;
            _swapB = (_swapB + 1) % _order.Length;
            StateHasChanged();
        }

        public void SwapAt(int a, int b)
        {
            EnsureSeeded();
            (_order![a], _order[b]) = (_order[b], _order[a]);
            StateHasChanged();
        }

        public void SetOrder(int[] order)
        {
            EnsureSeeded();
            _order = order;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            EnsureSeeded();
            var order = _order!;
            _scratch ??= new List<Component>(order.Length);
            _scratch.Clear();
            for (var i = 0; i < order.Length; i++)
            {
                _scratch.Add(GetOrCreateRow(order[i]));
            }

            return Div.Class("list")[_scratch];
        }

        private Component GetOrCreateRow(int key)
        {
            if (_rowsByKey.TryGetValue(key, out var row))
            {
                return row;
            }

            row = Div
                .Class("line")
                .Data(new Dictionary<string, string?> { ["rask-key"] = key.ToString() })[
                Span[$"Item {key}"]
            ];
            _rowsByKey[key] = row;
            return row;
        }

        private void EnsureSeeded()
        {
            if (_order is not null)
            {
                return;
            }

            _order = new int[InitialRowCount];
            for (var i = 0; i < InitialRowCount; i++)
            {
                _order[i] = i;
            }
        }
    }

    public sealed class BlazorKeyedList : ComponentBase
    {
        [Parameter] public int[] Order { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "list");
            for (var i = 0; i < Order.Length; i++)
            {
                var idx = Order[i];
                b.OpenElement(2, "div");
                b.SetKey(idx);
                b.AddAttribute(3, "class", "line");

                b.OpenElement(4, "span");
                b.AddContent(5, $"Item {idx}");
                b.CloseElement();

                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
