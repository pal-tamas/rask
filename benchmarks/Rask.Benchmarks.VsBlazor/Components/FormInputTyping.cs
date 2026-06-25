using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     A realistic small form: three labelled text inputs and a submit button. The
///     scenario re-renders after a single field's value mutates by one character
///     (the per-keystroke shape of any controlled form). The expected diff: one
///     <c>SetAttribute("value", newText)</c> op on the input whose model changed —
///     the surrounding labels, sibling inputs, and submit button must stay
///     untouched. Validates that the diff codec scopes attribute updates correctly
///     and that the per-element attribute-diff loop doesn't spuriously emit ops on
///     unchanged siblings.
/// </summary>
internal static class FormInputTyping
{
    public static Component BuildRask(string a, string b, string c)
    {
        return C.Form()[
            C.Label()["Field A"],
            C.Input<string>(InputType.Text, "a", a),
            C.Label()["Field B"],
            C.Input<string>(InputType.Text, "b", b),
            C.Label()["Field C"],
            C.Input<string>(InputType.Text, "c", c),
            C.Button("submit")["Save"]
        ];
    }

    public sealed class BlazorFormInputTyping : ComponentBase
    {
        [Parameter] public string A { get; set; } = "";
        [Parameter] public string B { get; set; } = "";
        [Parameter] public string C { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "form");

            b.OpenElement(1, "label");
            b.AddContent(2, "Field A");
            b.CloseElement();
            b.OpenElement(3, "input");
            b.AddAttribute(4, "type", "text");
            b.AddAttribute(5, "name", "a");
            b.AddAttribute(6, "value", A);
            b.CloseElement();

            b.OpenElement(7, "label");
            b.AddContent(8, "Field B");
            b.CloseElement();
            b.OpenElement(9, "input");
            b.AddAttribute(10, "type", "text");
            b.AddAttribute(11, "name", "b");
            b.AddAttribute(12, "value", B);
            b.CloseElement();

            b.OpenElement(13, "label");
            b.AddContent(14, "Field C");
            b.CloseElement();
            b.OpenElement(15, "input");
            b.AddAttribute(16, "type", "text");
            b.AddAttribute(17, "name", "c");
            b.AddAttribute(18, "value", C);
            b.CloseElement();

            b.OpenElement(19, "button");
            b.AddAttribute(20, "type", "submit");
            b.AddContent(21, "Save");
            b.CloseElement();

            b.CloseElement();
        }
    }
}
