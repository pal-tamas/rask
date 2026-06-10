using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     A 50-deep nest of identical divs. Stresses per-element overhead (open + close)
///     and the depth-stack walking the serializer/renderer does.
/// </summary>
internal static class NestedTree
{
    public static Component BuildRask(int depth)
    {
        var leaf = C.Span()["leaf"];
        for (var i = 0; i < depth; i++)
        {
            leaf = C.Div(Class: $"d{i}")[leaf];
        }

        return leaf;
    }

    public sealed class BlazorNestedTree : ComponentBase
    {
        [Parameter] public int Depth { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            for (var i = 0; i < Depth; i++)
            {
                b.OpenElement(0, "div");
                b.AddAttribute(1, "class", $"d{i}");
            }

            b.OpenElement(2, "span");
            b.AddContent(3, "leaf");
            b.CloseElement();

            for (var i = 0; i < Depth; i++)
            {
                b.CloseElement();
            }
        }
    }
}
