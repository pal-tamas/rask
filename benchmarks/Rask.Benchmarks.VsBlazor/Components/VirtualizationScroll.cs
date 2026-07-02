using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;
using RaskVirtualize = Rask.Core.Components.VirtualizeModel;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     1000-row list, item height 24px, viewport ~240px (10 rows visible). The Rask
///     side wires <c>Rask.Core.Components.VirtualizeModel&lt;int&gt;</c>; the Blazor side
///     renders every row with an explicit <c>@for</c> loop. We deliberately do NOT
///     compare against Blazor's own <c>Microsoft.AspNetCore.Components.Web.Virtualization.Virtualize&lt;T&gt;</c>
///     because that component reads viewport size via JS interop — without a live
///     browser host, it renders zero rows and the comparison is meaningless.
///     <para>
///         The intent is "rendering cost of a 1000-item list, windowed vs unwindowed,"
///         which is what an application author actually picks between when reaching
///         for virtualization. The headline number is the bytes/CPU savings windowing
///         buys; the Blazor app could match it by adopting Virtualize too, but
///         measuring that in-proc isn't possible.
///     </para>
/// </summary>
internal static class VirtualizationScroll
{
    public const int ItemCount = 1000;
    public const int ItemSizePx = 24;
    public const int ViewportHeightPx = 240;

    private static readonly FieldInfo ScrollTopField = typeof(RaskVirtualize)
                                                           .GetField("_scrollTop",
                                                               BindingFlags.Instance | BindingFlags.NonPublic)
                                                       ?? throw new InvalidOperationException(
                                                           "Rask.Core.Components.VirtualizeModel._scrollTop field not found");

    /// <summary>
    ///     Construct the Rask tree once. The returned root holds a reference to the
    ///     internal Virtualize instance so the caller can advance the simulated
    ///     scroll position via <see cref="SetScrollTop" /> between renders.
    /// </summary>
    public static (Component Root, RaskVirtualize Instance) BuildRask()
    {
        var items = new int[ItemCount];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = i;
        }

        var virt = C.VirtualizeModel(
            ctx =>
            {
                var rows = new List<Component>(ctx.VisibleItems.Count);
                foreach (var item in ctx.VisibleItems)
                {
                    rows.Add(C.Div(Class: "row", Id: $"r{item.Index}")[
                        C.Span()[$"Item {item.Index}"]
                    ]);
                }

                return C.Div(Class: "viewport", Style: $"height:{ViewportHeightPx}px;overflow:auto")[rows];
            },
            items,
            ItemSize: ItemSizePx,
            OverscanCount: 0,
            InitialClientHeight: ViewportHeightPx);

        return (virt, virt);
    }

    public static void SetScrollTop(RaskVirtualize virt, int scrollTop) =>
        ScrollTopField.SetValue(virt, scrollTop);

    public sealed class BlazorAllRows : ComponentBase
    {
        [Parameter] public int Count { get; set; } = ItemCount;
        [Parameter] public int Salt { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "viewport");
            b.AddAttribute(2, "style", $"height:{ViewportHeightPx}px;overflow:auto");
            for (var i = 0; i < Count; i++)
            {
                b.OpenElement(3, "div");
                b.AddAttribute(4, "class", "row");
                b.AddAttribute(5, "id", $"r{i}");
                b.OpenElement(6, "span");
                // Salt only affects one row's content so the diff scenario produces
                // a single text-node update against this baseline — equivalent in
                // intent to a virtualized window shift, but representable without
                // viewport semantics on the Blazor side.
                b.AddContent(7, i == 0 ? $"Item {i}-{Salt}" : $"Item {i}");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
