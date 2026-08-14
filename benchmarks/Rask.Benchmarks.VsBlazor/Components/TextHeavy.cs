using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     200 rows of plain ASCII text. Isolates the HTML encoder fast-path (Rask)
///     against Blazor's encoding cost on text nodes.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class TextHeavy
{
    public static Component BuildRask(int rowCount)
    {
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(P[$"line {i} of text content with no special chars"]);
        }

        return Div[rows];
    }

    public sealed class BlazorTextHeavy : ComponentBase
    {
        [Parameter] public int RowCount { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            for (var i = 0; i < RowCount; i++)
            {
                b.OpenElement(1, "p");
                b.AddContent(2, $"line {i} of text content with no special chars");
                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
