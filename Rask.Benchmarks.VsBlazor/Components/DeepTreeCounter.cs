using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     A counter at the bottom of a 50-deep div nest. Updating the counter must
///     emit a single <c>UpdateText</c> op whose <c>Path</c> is the depth-long
///     index array (50 zeros + the body slot + the text-node slot). Validates
///     that path-encoding overhead is proportional to depth — not to subtree
///     size — and that the diff codec doesn't accidentally descend into
///     unchanged ancestors. Counter-update at depth 0 already lives in
///     <c>CounterOnLargePage</c>; this row measures the path-tax explicitly.
/// </summary>
internal static class DeepTreeCounter
{
    public const int Depth = 50;

    public static Component BuildRask(int counter)
    {
        Component leaf = C.Span(Class: "counter")[counter.ToString()];
        for (var i = 0; i < Depth; i++)
        {
            leaf = C.Div(Class: $"d{i}")[leaf];
        }

        return C.Fragment()[C.Doctype(), C.Html()[C.Body()[leaf]]];
    }

    public sealed class BlazorDeepTreeCounter : ComponentBase
    {
        [Parameter] public int Counter { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            for (var i = 0; i < Depth; i++)
            {
                b.OpenElement(0, "div");
                b.AddAttribute(1, "class", $"d{i}");
            }

            b.OpenElement(2, "span");
            b.AddAttribute(3, "class", "counter");
            b.AddContent(4, Counter.ToString());
            b.CloseElement();

            for (var i = 0; i < Depth; i++)
            {
                b.CloseElement();
            }
        }
    }
}
