using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Conditional rendering — a 50-item panel appears between a fixed header and
///     footer when a boolean flag flips. Models the "expand/collapse" or
///     "show/hide details" pattern that drives a lot of real app UI: a state bit
///     materialises or removes a substantial subtree mid-page. The rows do NOT
///     carry <c>data-rask-key</c>, so the diff codec falls into its positional
///     branch and emits <c>InsertSubtree</c>/<c>RemoveSubtree</c> ops with the
///     full HTML fragment — which then routes through the live-session gate to
///     the full-HTML morph path (untrusted structural ops). The benchmark exposes
///     both raw-diff bytes and full-HTML bytes so the
///     <c>min(diff, full)</c> calculation in <c>vs-blazor.md</c> stays honest
///     about what the wire actually carries.
/// </summary>
internal static class ConditionalPanel
{
    public const int PanelRowCount = 50;

    public static Component BuildRask(bool showPanel)
    {
        var rows = new List<Child>(PanelRowCount);
        for (var i = 0; i < PanelRowCount; i++)
        {
            rows.Add(C.Li(Class: "panel-row")[$"Detail {i}"]);
        }

        var body = new List<Child>(3)
        {
            C.Header()[C.H1()["Dashboard"]]
        };
        if (showPanel)
        {
            body.Add(C.Div(Class: "panel")[C.Ul()[rows]]);
        }
        body.Add(C.Footer()[C.Span()["© Rask"]]);

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "shell")[body]]]
        ];
    }

    public sealed class BlazorConditionalPanel : ComponentBase
    {
        [Parameter] public bool ShowPanel { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "shell");

            b.OpenElement(2, "header");
            b.OpenElement(3, "h1");
            b.AddContent(4, "Dashboard");
            b.CloseElement();
            b.CloseElement();

            if (ShowPanel)
            {
                b.OpenElement(5, "div");
                b.AddAttribute(6, "class", "panel");
                b.OpenElement(7, "ul");
                for (var i = 0; i < PanelRowCount; i++)
                {
                    b.OpenElement(8, "li");
                    b.AddAttribute(9, "class", "panel-row");
                    b.AddContent(10, $"Detail {i}");
                    b.CloseElement();
                }
                b.CloseElement();
                b.CloseElement();
            }

            b.OpenElement(11, "footer");
            b.OpenElement(12, "span");
            b.AddContent(13, "© Rask");
            b.CloseElement();
            b.CloseElement();

            b.CloseElement();
        }
    }
}
