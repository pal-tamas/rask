using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Companion scenarios to <see cref="KeyedList"/> reorder: one row appended at
///     the end of a 100-row keyed list (or, in the delete variant, one row removed
///     from the middle). Stresses the <c>InsertSubtree</c> / <c>RemoveSubtree</c>
///     diff op kinds — the cases the live-diff gate explicitly checks for before
///     deciding to ship a diff vs full-HTML payload.
/// </summary>
internal static class AppendDeleteRowChurn
{
    public const int InitialRowCount = 100;

    /// <summary>
    ///     Build a Rask tree from a row id sequence. Identical row shape to
    ///     <see cref="KeyedList"/> so the differ can match them across renders by
    ///     the <c>rask-key</c> data attribute.
    /// </summary>
    public static Component BuildRask(int[] order)
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

        return C.Div(Class: "list")[rows];
    }

    // Stateful counterpart for the LiveDiff harness — caches row Child wrappers by
    // key and only swaps the visible order array on Mutate. Matches Blazor's
    // ParameterView-update path (which also reuses its child components across
    // parameter changes) instead of paying for 100+ fresh element allocations per
    // benchmark iteration on the Rask side.
#pragma warning disable RASK014
    public sealed class StatefulAppendDeleteList : Component
#pragma warning restore RASK014
    {
        public int Capacity { get; init; } = InitialRowCount + 1;

        private Child[]? _rowsByKey;
        private int[]? _currentOrder;
        private List<Child>? _scratch;

        public int[] CurrentOrder
        {
            get
            {
                EnsureSeeded();
                return _currentOrder!;
            }
        }

        public void SetOrder(int[] order)
        {
            EnsureSeeded();
            _currentOrder = order;
            StateHasChanged();
        }

        protected override RenderResult Render()
        {
            EnsureSeeded();
            var order = _currentOrder!;
            _scratch ??= new List<Child>(order.Length);
            _scratch.Clear();
            for (var i = 0; i < order.Length; i++)
            {
                _scratch.Add(_rowsByKey![order[i]]);
            }
            return C.Div(Class: "list")[_scratch];
        }

        private void EnsureSeeded()
        {
            if (_rowsByKey is not null) return;
            _rowsByKey = new Child[Capacity];
            for (var i = 0; i < Capacity; i++)
            {
                _rowsByKey[i] = C.Div(
                    Class: "row",
                    Data: new Dictionary<string, string?> { ["rask-key"] = i.ToString() })[
                    C.Span()[$"Item {i}"]
                ];
            }
            _currentOrder ??= InitInitialOrder();
        }

        private static int[] InitInitialOrder()
        {
            var o = new int[InitialRowCount];
            for (var i = 0; i < InitialRowCount; i++) o[i] = i;
            return o;
        }
    }

    public sealed class BlazorAppendDeleteList : ComponentBase
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
                b.AddAttribute(3, "class", "row");

                b.OpenElement(4, "span");
                b.AddContent(5, $"Item {idx}");
                b.CloseElement();

                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
