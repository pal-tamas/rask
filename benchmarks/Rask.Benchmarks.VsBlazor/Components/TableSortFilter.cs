using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     200-row keyed table. Two state transitions per iteration:
///     <list type="number">
///         <item>
///             <description>Column sort flips the row order (reverse).</description>
///         </item>
///         <item>
///             <description>Filter halves the visible rows (drops the second half).</description>
///         </item>
///     </list>
///     Combined keyed reorder + structural remove pattern — the most challenging
///     compound diff scenario. Keyed reorder produces MoveSubtree ops (trusted);
///     filter produces RemoveSubtree ops on positional siblings (untrusted, so the
///     gate routes through full HTML for the filter step).
/// </summary>
internal static class TableSortFilter
{
    public const int InitialRowCount = 200;

#pragma warning disable RASK014
    public sealed class StatefulTableSortFilter : Component
#pragma warning restore RASK014
    {
        private readonly Dictionary<int, Child> _rowCache = new();
        private List<Child>? _scratch;

        public StatefulTableSortFilter()
        {
            VisibleOrder = new int[InitialRowCount];
            for (var i = 0; i < InitialRowCount; i++)
            {
                VisibleOrder[i] = i;
            }
        }

        public int[] VisibleOrder { get; private set; }

        public void ReverseSort()
        {
            Array.Reverse(VisibleOrder);
            StateHasChanged();
        }

        public void HalveFilter()
        {
            var keep = VisibleOrder.Length / 2;
            var trimmed = new int[keep];
            Array.Copy(VisibleOrder, trimmed, keep);
            VisibleOrder = trimmed;
            StateHasChanged();
        }

        public void Reset()
        {
            VisibleOrder = new int[InitialRowCount];
            for (var i = 0; i < InitialRowCount; i++)
            {
                VisibleOrder[i] = i;
            }

            StateHasChanged();
        }

        protected override RenderResult Render()
        {
            _scratch ??= new List<Child>(VisibleOrder.Length);
            _scratch.Clear();
            for (var i = 0; i < VisibleOrder.Length; i++)
            {
                _scratch.Add(GetOrCreateRow(VisibleOrder[i]));
            }

            return C.Div(Class: "table-shell")[
                C.Table()[
                    C.Thead()[C.Tr()[
                        C.Th()["ID"],
                        C.Th()["Name"],
                        C.Th()["Price"],
                        C.Th()["Status"]
                    ]],
                    C.Tbody()[_scratch]
                ]
            ];
        }

        private Child GetOrCreateRow(int id)
        {
            if (_rowCache.TryGetValue(id, out var row))
            {
                return row;
            }

            row = C.Tr(
                Data: new Dictionary<string, string?> { ["rask-key"] = id.ToString() })[
                C.Td()[$"{id}"],
                C.Td()[$"Item {id}"],
                C.Td()[$"${(id * 7) + 13}"],
                C.Td()[id % 2 == 0 ? "active" : "idle"]
            ];
            _rowCache[id] = row;
            return row;
        }
    }

    public sealed class BlazorTableSortFilter : ComponentBase
    {
        [Parameter] public int[] Order { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "table-shell");

            b.OpenElement(2, "table");

            b.OpenElement(3, "thead");
            b.OpenElement(4, "tr");
            string[] heads = ["ID", "Name", "Price", "Status"];
            foreach (var h in heads)
            {
                b.OpenElement(5, "th");
                b.AddContent(6, h);
                b.CloseElement();
            }

            b.CloseElement();
            b.CloseElement();

            b.OpenElement(7, "tbody");
            for (var i = 0; i < Order.Length; i++)
            {
                var id = Order[i];
                b.OpenElement(8, "tr");
                b.SetKey(id);
                b.OpenElement(9, "td");
                b.AddContent(10, $"{id}");
                b.CloseElement();
                b.OpenElement(11, "td");
                b.AddContent(12, $"Item {id}");
                b.CloseElement();
                b.OpenElement(13, "td");
                b.AddContent(14, $"${(id * 7) + 13}");
                b.CloseElement();
                b.OpenElement(15, "td");
                b.AddContent(16, id % 2 == 0 ? "active" : "idle");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();

            b.CloseElement(); // table
            b.CloseElement(); // div
        }
    }
}
