using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Toggles between a parent that renders <c>ActiveCount</c> user-Component
///     children and one that renders zero. Stresses the user-Component branch of
///     <c>HtmlSerializer</c> (the <c>RenderForLive</c> caching path + per-child
///     slot reuse), the structural-diff gate (<c>InsertSubtree</c> /
///     <c>RemoveSubtree</c> over user-component subtrees), and Blazor's lifecycle
///     diff for the same shape.
///     <para>
///         Note: this benchmark walks the diff codec through
///         <c>RaskHarness</c>, which serializes via <c>HtmlSerializer</c> directly
///         and therefore does NOT invoke <c>OnMount</c> / <c>OnUnmount</c>
///         lifecycle hooks — those fire only under <c>RenderAsLiveRoot</c>. The
///         comparison measures wire-format and rendering cost for the user-Component
///         path; full live-lifecycle cost belongs in <c>Rask.Benchmarks</c>'s
///         round-trip suite (no Blazor counterpart there yet).
///     </para>
/// </summary>
internal static class LifecycleChurn
{
    public const int MaxActiveCount = 100;

#pragma warning disable RASK014
    public sealed class RaskChild : Component
#pragma warning restore RASK014
    {
        public int Index { get; set; }

        protected override RenderResult Render() =>
            C.Div(Class: "child", Id: $"c{Index}")[
                C.Span(Class: "label")[$"Child {Index}"]
            ];
    }

    public static Component BuildRask(int activeCount)
    {
        var children = new List<Child>(activeCount);
        for (var i = 0; i < activeCount; i++)
        {
#pragma warning disable RASK014
            children.Add(new RaskChild { Index = i });
#pragma warning restore RASK014
        }

        return C.Div(Class: "host")[children];
    }

    public sealed class BlazorChild : ComponentBase
    {
        [Parameter] public int Index { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "child");
            b.AddAttribute(2, "id", $"c{Index}");
            b.OpenElement(3, "span");
            b.AddAttribute(4, "class", "label");
            b.AddContent(5, $"Child {Index}");
            b.CloseElement();
            b.CloseElement();
        }
    }

    public sealed class BlazorLifecycleChurn : ComponentBase
    {
        [Parameter] public int ActiveCount { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "host");
            for (var i = 0; i < ActiveCount; i++)
            {
                b.OpenComponent<BlazorChild>(2);
                b.AddComponentParameter(3, nameof(BlazorChild.Index), i);
                b.SetKey(i);
                b.CloseComponent();
            }

            b.CloseElement();
        }
    }
}
