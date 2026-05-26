using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     A theme switch on a moderate tree: one state bit flips five attributes on the
///     root element at once (class, style, four <c>data-*</c> theme tokens). Models
///     the typical "dark mode toggle" or "compact density" pattern. The expected
///     diff: exactly five <c>SetAttribute</c> ops scoped to the root, all other
///     descendants (an info card and a button) stay quiet. Validates that the
///     per-element attribute diff in <c>FrameDiffer.DiffAttributes</c> picks up all
///     N changes without falling out into a sibling Remove/Insert.
/// </summary>
internal static class ThemeSwitch
{
    public static Component BuildRask(bool dark)
    {
        var cls = dark ? "app dark" : "app light";
        var style = dark ? "background:#111;color:#eee" : "background:#fff;color:#222";
        var data = new Dictionary<string, string?>
        {
            ["theme-mode"] = dark ? "dark" : "light",
            ["theme-density"] = dark ? "compact" : "comfortable",
            ["theme-accent"] = dark ? "indigo" : "amber",
            ["theme-contrast"] = dark ? "high" : "normal"
        };

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[
                C.Div(Class: cls, Style: style, Data: data)[
                    C.Section(Class: "card")[
                        C.H2()["Welcome"],
                        C.P()["Pick a theme to taste."]
                    ],
                    C.Button(Type: "button")[$"Toggle ({(dark ? "dark" : "light")})"]
                ]
            ]]
        ];
    }

    public sealed class BlazorThemeSwitch : ComponentBase
    {
        [Parameter] public bool Dark { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", Dark ? "app dark" : "app light");
            b.AddAttribute(2, "style", Dark ? "background:#111;color:#eee" : "background:#fff;color:#222");
            b.AddAttribute(3, "data-theme-mode", Dark ? "dark" : "light");
            b.AddAttribute(4, "data-theme-density", Dark ? "compact" : "comfortable");
            b.AddAttribute(5, "data-theme-accent", Dark ? "indigo" : "amber");
            b.AddAttribute(6, "data-theme-contrast", Dark ? "high" : "normal");

            b.OpenElement(7, "section");
            b.AddAttribute(8, "class", "card");

            b.OpenElement(9, "h2");
            b.AddContent(10, "Welcome");
            b.CloseElement();

            b.OpenElement(11, "p");
            b.AddContent(12, "Pick a theme to taste.");
            b.CloseElement();

            b.CloseElement();

            b.OpenElement(13, "button");
            b.AddAttribute(14, "type", "button");
            b.AddContent(15, $"Toggle ({(Dark ? "dark" : "light")})");
            b.CloseElement();

            b.CloseElement();
        }
    }
}
