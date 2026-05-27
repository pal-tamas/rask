using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     10-field form. Each iteration: one field's value mutates and the validation
///     message div under that field toggles between empty and "required". Validates
///     that input value updates (SetAttribute) coexist with structural insert/remove of
///     a sibling validation message (positional, untrusted — routes to full HTML when
///     the message div appears or disappears, but field-value-only toggles ship as diff).
/// </summary>
internal static class FormValidationChurn
{
    public const int FieldCount = 10;

#pragma warning disable RASK014
    public sealed class StatefulForm : Component
#pragma warning restore RASK014
    {
        private readonly string[] _values = new string[FieldCount];
        private readonly bool[] _invalid = new bool[FieldCount];

        public void MutateField(int index)
        {
            _values[index] = $"v{(_values[index]?.Length ?? 0) + 1}";
            _invalid[index] = !_invalid[index];
            StateHasChanged();
        }

        protected override RenderResult Render()
        {
            var children = new List<Child>(FieldCount * 2 + 1);
            for (var i = 0; i < FieldCount; i++)
            {
                var fieldClass = _invalid[i] ? "field invalid" : "field";
                children.Add(C.Div(Class: fieldClass, Id: $"f{i}")[
                    C.Label()[$"Field {i}"],
                    C.Input(Type: "text", Value: _values[i] ?? string.Empty),
                    _invalid[i]
                        ? C.Div(Class: "validation-msg")["required"]
                        : C.Div(Class: "validation-msg")
                ]);
            }
            children.Add(C.Button(Type: "submit")["Save"]);

            return C.Fragment()[
                C.Doctype(),
                C.Html()[C.Body()[
                    C.Form()[children]
                ]]
            ];
        }
    }

    public sealed class BlazorForm : ComponentBase
    {
        [Parameter] public string?[] Values { get; set; } = new string?[FieldCount];
        [Parameter] public bool[] Invalid { get; set; } = new bool[FieldCount];

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "form");
            for (var i = 0; i < FieldCount; i++)
            {
                var fieldClass = Invalid[i] ? "field invalid" : "field";
                b.OpenElement(1, "div");
                b.AddAttribute(2, "class", fieldClass);
                b.AddAttribute(3, "id", $"f{i}");
                b.OpenElement(4, "label");
                b.AddContent(5, $"Field {i}");
                b.CloseElement();
                b.OpenElement(6, "input");
                b.AddAttribute(7, "type", "text");
                b.AddAttribute(8, "value", Values[i] ?? string.Empty);
                b.CloseElement();
                b.OpenElement(9, "div");
                b.AddAttribute(10, "class", "validation-msg");
                if (Invalid[i]) b.AddContent(11, "required");
                b.CloseElement();
                b.CloseElement();
            }
            b.OpenElement(12, "button");
            b.AddAttribute(13, "type", "submit");
            b.AddContent(14, "Save");
            b.CloseElement();
            b.CloseElement();
        }
    }
}
