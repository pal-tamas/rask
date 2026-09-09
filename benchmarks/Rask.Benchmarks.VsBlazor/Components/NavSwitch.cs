using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     5-tab nav. Each iteration switches the active tab, swapping the entire main-content
///     subtree (each tab renders 40 rows of distinct content). Tests large structural
///     insert+remove in a single render — the diff codec emits InsertSubtree/RemoveSubtree
///     positional ops (untrusted), which the live-session gate routes through full-HTML
///     fallback. The pinned benchmark therefore measures the FULL render cost as that's
///     what production ships for this pattern.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class NavSwitch
{
    public const int TabCount = 5;
    public const int RowsPerTab = 40;

    public static Component BuildRask(int activeTab)
    {
        var tabs = new List<Component>(TabCount);
        for (var t = 0; t < TabCount; t++)
        {
            var isActive = t == activeTab;
            tabs.Add(Li.Class(isActive ? "tab active" : "tab")[
                A.Href($"#t{t}")[$"Tab {t}"]
            ]);
        }

        var contentRows = new List<Component>(RowsPerTab);
        for (var i = 0; i < RowsPerTab; i++)
        {
            contentRows.Add(Div.Class("line")[
                Span.Class("label")[$"Tab {activeTab} row {i}"],
                A.Href($"/tab/{activeTab}/{i}")[$"open {i}"]
            ]);
        }

        return Div.Class("nav-shell")[
            Nav[Ul[tabs]],
            Main.Id($"tab-{activeTab}")[contentRows]
        ];
    }

#pragma warning disable RASK014
    public sealed partial class StatefulNavSwitch : Component
#pragma warning restore RASK014
    {
        private readonly List<Component>?[] _tabContentCache = new List<Component>?[TabCount];

        public int ActiveTab { get; private set; }

        public void Switch(int tab)
        {
            ActiveTab = tab;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            var tabs = new List<Component>(TabCount);
            for (var t = 0; t < TabCount; t++)
            {
                var isActive = t == ActiveTab;
                tabs.Add(Li.Class(isActive ? "tab active" : "tab")[
                    A.Href($"#t{t}")[$"Tab {t}"]
                ]);
            }

            var content = _tabContentCache[ActiveTab];
            if (content is null)
            {
                content = new List<Component>(RowsPerTab);
                for (var i = 0; i < RowsPerTab; i++)
                {
                    content.Add(Div.Class("line")[
                        Span.Class("label")[$"Tab {ActiveTab} row {i}"],
                        A.Href($"/tab/{ActiveTab}/{i}")[$"open {i}"]
                    ]);
                }

                _tabContentCache[ActiveTab] = content;
            }

            return Div.Class("nav-shell")[
                Nav[Ul[tabs]],
                Main.Id($"tab-{ActiveTab}")[content]
            ];
        }
    }

    public sealed class BlazorNavSwitch : ComponentBase
    {
        [Parameter] public int ActiveTab { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "nav-shell");

            b.OpenElement(2, "nav");
            b.OpenElement(3, "ul");
            for (var t = 0; t < TabCount; t++)
            {
                var isActive = t == ActiveTab;
                b.OpenElement(4, "li");
                b.AddAttribute(5, "class", isActive ? "tab active" : "tab");
                b.OpenElement(6, "a");
                b.AddAttribute(7, "href", $"#t{t}");
                b.AddContent(8, $"Tab {t}");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();
            b.CloseElement();

            b.OpenElement(9, "main");
            b.AddAttribute(10, "id", $"tab-{ActiveTab}");
            for (var i = 0; i < RowsPerTab; i++)
            {
                b.OpenElement(11, "div");
                b.AddAttribute(12, "class", "line");
                b.OpenElement(13, "span");
                b.AddAttribute(14, "class", "label");
                b.AddContent(15, $"Tab {ActiveTab} row {i}");
                b.CloseElement();
                b.OpenElement(16, "a");
                b.AddAttribute(17, "href", $"/tab/{ActiveTab}/{i}");
                b.AddContent(18, $"open {i}");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();

            b.CloseElement();
        }
    }
}
