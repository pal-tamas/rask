using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     100 sibling rows; a single boolean state bit toggles a <c>data-loaded</c>
///     attribute onto every row at once. The diff emits 100 <c>SetAttribute</c>
///     ops (one per row) all with the same attribute name — exposes the wire-byte
///     cost of repeating <c>"data-loaded"</c> in every op. This is the canonical
///     case for a future per-payload attribute-name symbol table; the benchmark
///     baselines the "before" so the optimisation's impact is measurable.
/// </summary>
internal static class AttributeBurstUpdate
{
    public const int RowCount = 100;
    private const string AttrName = "data-loaded";

    public static Component BuildRask(bool loaded)
    {
        var rows = new List<Child>(RowCount);
        for (var i = 0; i < RowCount; i++)
        {
            Component row = loaded
                ? C.Div(Class: "row", Data: new Dictionary<string, string?> { ["loaded"] = "true" })[
                    C.Span()[$"Row {i}"]
                ]
                : C.Div(Class: "row")[
                    C.Span()[$"Row {i}"]
                ];
            rows.Add(row);
        }

        return C.Div(Class: "rows")[rows];
    }

    public sealed class BlazorAttributeBurst : ComponentBase
    {
        [Parameter] public bool Loaded { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "rows");
            for (var i = 0; i < RowCount; i++)
            {
                b.OpenElement(2, "div");
                b.AddAttribute(3, "class", "row");
                if (Loaded)
                {
                    b.AddAttribute(4, AttrName, "true");
                }

                b.OpenElement(5, "span");
                b.AddContent(6, $"Row {i}");
                b.CloseElement();

                b.CloseElement();
            }
            b.CloseElement();
        }
    }
}
