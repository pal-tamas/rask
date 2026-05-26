using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     One element in a moderate tree gains/loses a CSS class. Models the most
///     common UI mutation in real apps — route highlight, hover state, validation
///     glow, dark-mode toggle on a child. The expected diff: one
///     <c>SetAttribute("class", ...)</c> op on the affected element. The
///     surrounding 19 sidebar-item elements must stay out of the diff. Distinct
///     from <c>AttributeUpdate</c> in that it targets <c>class</c> specifically
///     (a real-world hot attribute with strict equality semantics) and uses a
///     navigation-shaped tree rather than an attribute-heavy synthetic one.
/// </summary>
internal static class ClassToggle
{
    public const int SidebarItemCount = 20;

    public static Component BuildRask(int activeIndex)
    {
        var items = new List<Child>(SidebarItemCount);
        for (var i = 0; i < SidebarItemCount; i++)
        {
            var cls = i == activeIndex ? "nav-item active" : "nav-item";
            items.Add(C.Li(Class: cls)[
                C.A(Href: $"/page/{i}")[$"Page {i}"]
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[
                C.Nav(Class: "sidebar")[C.Ul()[items]]
            ]]
        ];
    }

    public sealed class BlazorClassToggle : ComponentBase
    {
        [Parameter] public int ActiveIndex { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "nav");
            b.AddAttribute(1, "class", "sidebar");
            b.OpenElement(2, "ul");
            for (var i = 0; i < SidebarItemCount; i++)
            {
                b.OpenElement(3, "li");
                b.AddAttribute(4, "class", i == ActiveIndex ? "nav-item active" : "nav-item");

                b.OpenElement(5, "a");
                b.AddAttribute(6, "href", $"/page/{i}");
                b.AddContent(7, $"Page {i}");
                b.CloseElement();

                b.CloseElement();
            }
            b.CloseElement();
            b.CloseElement();
        }
    }
}
