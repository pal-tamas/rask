using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     100 keyed rows. Re-renders shuffle two entries; the diff should encode the move
///     rather than the whole list. Rask uses data-rask-key; Blazor uses SetKey().
/// </summary>
internal static class KeyedList
{
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

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "list")[rows]]]
        ];
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
