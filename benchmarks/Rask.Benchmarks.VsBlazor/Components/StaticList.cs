using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     A list of N identically-shaped rows. Scales the render hot path linearly. Used at
///     [Params(5, 100, 1000)] to expose any per-row constant in either framework.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class StaticList
{
    public static Component BuildRask(int rowCount)
    {
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(Div.Class("row").Id($"r{i}")[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"]
            ]);
        }

        return Div.Class("list")[rows];
    }

    public sealed class BlazorStaticList : ComponentBase
    {
        [Parameter] public int RowCount { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "list");
            for (var i = 0; i < RowCount; i++)
            {
                b.OpenElement(2, "div");
                b.AddAttribute(3, "class", "row");
                b.AddAttribute(4, "id", $"r{i}");

                b.OpenElement(5, "span");
                b.AddAttribute(6, "class", "label");
                b.AddContent(7, $"Item {i}");
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
