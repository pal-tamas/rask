using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Nested keyed lists — an outer keyed list of "card" elements, each containing
///     an inner keyed list of rows. Reordering two outer cards must NOT cascade into
///     ops on the inner trees: the keyed differ moves whole subtrees by key while
///     the inner content (identical across the swap) produces zero recursive ops.
///     Validates that keyed matching composes through nesting — same scenario class
///     a typical data table (rows of expandable groups) renders into.
/// </summary>
internal static class NestedKeyedList
{
    public const int OuterCardCount = 20;
    public const int InnerRowCount = 5;

    public static Component BuildRask(int[] outerOrder)
    {
        var cards = new List<Child>(outerOrder.Length);
        for (var i = 0; i < outerOrder.Length; i++)
        {
            var cardKey = outerOrder[i];
            var rows = new List<Child>(InnerRowCount);
            for (var r = 0; r < InnerRowCount; r++)
            {
                rows.Add(C.Li(
                    Class: "row",
                    Data: new Dictionary<string, string?> { ["rask-key"] = $"{cardKey}.{r}" })[
                    C.Span()[$"Card {cardKey} · row {r}"]
                ]);
            }

            cards.Add(C.Div(
                Class: "card",
                Data: new Dictionary<string, string?> { ["rask-key"] = cardKey.ToString() })[
                C.H3()[$"Card {cardKey}"],
                C.Ul()[rows]
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "deck")[cards]]]
        ];
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
