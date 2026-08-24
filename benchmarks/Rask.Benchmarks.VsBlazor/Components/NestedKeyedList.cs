using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Nested keyed lists — an outer keyed list of "card" elements, each containing
///     an inner keyed list of rows. Reordering two outer cards must NOT cascade into
///     ops on the inner trees: the keyed differ moves whole subtrees by key while
///     the inner content (identical across the swap) produces zero recursive ops.
///     Validates that keyed matching composes through nesting — same scenario class
///     a typical data table (rows of expandable groups) renders into.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class NestedKeyedList
{
    public const int OuterCardCount = 20;
    public const int InnerRowCount = 5;

    public static Component BuildRask(int[] outerOrder)
    {
        var cards = new List<Component>(outerOrder.Length);
        for (var i = 0; i < outerOrder.Length; i++)
        {
            var cardKey = outerOrder[i];
            var rows = new List<Component>(InnerRowCount);
            for (var r = 0; r < InnerRowCount; r++)
            {
                rows.Add(Li
                    .Class("row")
                    .Data(new Dictionary<string, string?> { ["rask-key"] = $"{cardKey}.{r}" })[
                    Span[$"Card {cardKey} · row {r}"]
                ]);
            }

            cards.Add(Div
                .Class("card")
                .Data(new Dictionary<string, string?> { ["rask-key"] = cardKey.ToString() })[
                H3[$"Card {cardKey}"],
                Ul[rows]
            ]);
        }

        return Div.Class("deck")[cards];
    }

    // Stateful counterpart used by the live-diff harness. Caches each card (with its
    // inner Ul + 5 rows) by key once; benchmark mutations swap two entries of the
    // outer-order array. Mirrors Blazor's ParameterView reuse path.
#pragma warning disable RASK014
    public sealed partial class StatefulNestedKeyedList : Component
#pragma warning restore RASK014
    {
        private Component[]? _cardsByKey;
        private int[]? _order;
        private List<Component>? _scratch;
        private int _swapA = 3;
        private int _swapB;
        public int OuterCapacity { get; init; } = OuterCardCount;

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
            _swapB = _swapB == 0 ? OuterCapacity - 3 : _swapB;
            (_order![_swapA], _order[_swapB]) = (_order[_swapB], _order[_swapA]);
            _swapA = (_swapA + 1) % _order.Length;
            _swapB = (_swapB + 1) % _order.Length;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            EnsureSeeded();
            _scratch ??= new List<Component>(OuterCapacity);
            _scratch.Clear();
            for (var i = 0; i < _order!.Length; i++)
            {
                _scratch.Add(_cardsByKey![_order[i]]);
            }

            return Div.Class("deck")[_scratch];
        }

        private void EnsureSeeded()
        {
            if (_cardsByKey is not null)
            {
                return;
            }

            _cardsByKey = new Component[OuterCapacity];
            for (var k = 0; k < OuterCapacity; k++)
            {
                var rows = new List<Component>(InnerRowCount);
                for (var r = 0; r < InnerRowCount; r++)
                {
                    rows.Add(Li
                        .Class("row")
                        .Data(new Dictionary<string, string?> { ["rask-key"] = $"{k}.{r}" })[
                        Span[$"Card {k} · row {r}"]
                    ]);
                }

                _cardsByKey[k] = Div
                    .Class("card")
                    .Data(new Dictionary<string, string?> { ["rask-key"] = k.ToString() })[
                    H3[$"Card {k}"],
                    Ul[rows]
                ];
            }

            if (_order is null)
            {
                _order = new int[OuterCapacity];
                for (var i = 0; i < OuterCapacity; i++)
                {
                    _order[i] = i;
                }
            }
        }
    }

    public sealed class BlazorNestedKeyedList : ComponentBase
    {
        [Parameter] public int[] OuterOrder { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "deck");
            for (var i = 0; i < OuterOrder.Length; i++)
            {
                var cardKey = OuterOrder[i];
                b.OpenElement(2, "div");
                b.SetKey(cardKey);
                b.AddAttribute(3, "class", "card");

                b.OpenElement(4, "h3");
                b.AddContent(5, $"Card {cardKey}");
                b.CloseElement();

                b.OpenElement(6, "ul");
                for (var r = 0; r < InnerRowCount; r++)
                {
                    b.OpenElement(7, "li");
                    b.SetKey($"{cardKey}.{r}");
                    b.AddAttribute(8, "class", "row");

                    b.OpenElement(9, "span");
                    b.AddContent(10, $"Card {cardKey} · row {r}");
                    b.CloseElement();

                    b.CloseElement();
                }

                b.CloseElement();

                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
