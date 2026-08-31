using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     200 static rows + one dynamic counter cell. The headline scenario: most of the
///     DOM is unchanged across renders, only one text node mutates. This is what the
///     Rask diff codec advertises 1,802× wire-byte reduction on.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class LargePageWithCounter
{
    public const int LargePageRowCount = 200;

    public static Component BuildRask(int counter)
    {
        var rows = new List<Component>(LargePageRowCount);
        for (var i = 0; i < LargePageRowCount; i++)
        {
            rows.Add(Div.Class("line").Id($"r{i}")[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
            ]);
        }

        return Div.Class("wrap").Id("root")[
            Div.Class("counter").Id("counter")[
                Span.Class("value")[counter.ToString()]
            ],
            Div.Class("body")[rows]
        ];
    }

    /// <summary>
    ///     Same shape but the changing value lives deep inside the row list, exercising
    ///     the path-walk-through-200-elements case.
    /// </summary>
    public static Component BuildRaskWithDeepTextCell(int counter)
    {
        var rows = new List<Component>(LargePageRowCount);
        for (var i = 0; i < LargePageRowCount; i++)
        {
            var text = i == LargePageRowCount / 2 ? $"ticker {counter}" : $"Item {i}";
            rows.Add(Div.Class("line").Id($"r{i}")[
                Span.Class("label")[text],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
            ]);
        }

        return Div.Class("body")[rows];
    }

    public sealed class BlazorLargePageWithCounter : ComponentBase
    {
        [Parameter] public int Counter { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "wrap");
            b.AddAttribute(2, "id", "root");

            b.OpenElement(3, "div");
            b.AddAttribute(4, "class", "counter");
            b.AddAttribute(5, "id", "counter");
            b.OpenElement(6, "span");
            b.AddAttribute(7, "class", "value");
            b.AddContent(8, Counter);
            b.CloseElement();
            b.CloseElement();

            b.OpenElement(9, "div");
            b.AddAttribute(10, "class", "body");
            for (var i = 0; i < LargePageRowCount; i++)
            {
                b.OpenElement(11, "div");
                b.AddAttribute(12, "class", "line");
                b.AddAttribute(13, "id", $"r{i}");

                b.OpenElement(14, "span");
                b.AddAttribute(15, "class", "label");
                b.AddContent(16, $"Item {i}");
                b.CloseElement();

                b.OpenElement(17, "a");
                b.AddAttribute(18, "href", $"/item/{i}");
                b.AddAttribute(19, "class", "lnk");
                b.AddContent(20, $"open {i}");
                b.CloseElement();

                b.CloseElement();
            }

            b.CloseElement();

            b.CloseElement();
        }
    }

    public sealed class BlazorLargePageWithDeepTextCell : ComponentBase
    {
        [Parameter] public int Counter { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "body");
            for (var i = 0; i < LargePageRowCount; i++)
            {
                var text = i == LargePageRowCount / 2 ? $"ticker {Counter}" : $"Item {i}";
                b.OpenElement(2, "div");
                b.AddAttribute(3, "class", "line");
                b.AddAttribute(4, "id", $"r{i}");

                b.OpenElement(5, "span");
                b.AddAttribute(6, "class", "label");
                b.AddContent(7, text);
                b.CloseElement();

                b.OpenElement(8, "a");
                b.AddAttribute(9, "href", $"/item/{i}");
                b.AddAttribute(10, "class", "lnk");
                b.AddContent(11, $"open {i}");
                b.CloseElement();

                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
