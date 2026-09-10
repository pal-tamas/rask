using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Counter scenario: one button + one span holding an integer value. The minimal
///     "state mutates, one node updates" pair for the diff codec.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class Counter
{
    public static Component BuildRask(int value) =>
        Div.Class("counter").Id("counter")[
            Span.Class("value")[value.ToString()],
            Button.Class("inc")["+"]
        ];

    public sealed class BlazorCounter : ComponentBase
    {
        [Parameter] public int Value { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "counter");
            b.AddAttribute(2, "id", "counter");

            b.OpenElement(3, "span");
            b.AddAttribute(4, "class", "value");
            b.AddContent(5, Value);
            b.CloseElement();

            b.OpenElement(6, "button");
            b.AddAttribute(7, "class", "inc");
            b.AddContent(8, "+");
            b.CloseElement();

            b.CloseElement();
        }
    }
}
