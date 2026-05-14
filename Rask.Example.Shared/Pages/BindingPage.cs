using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("binding")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BindingPage : Component
{
    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Two-way binding",
                "Inputs can be wired with plain Value + OnInput, or with a strongly-typed Bind expression that resolves the input name, type, and update timing for you."),
            H2(Class: "h4 mt-4 mb-3")["Manual — Value + OnInput"],
            CodeSample(
                """
                Input(Type: "text",
                      Value: _typed,
                      OnInput: v => _typed = v)
                P()[$"Echo: {_typed}"]
                """,
                Notes:
                "The low-level path: wire Value and the event handler yourself. Works for any input type, but you parse and re-render manually.",
                Result: BindingManualDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — Input<TProp>(Bind: ...)"],
            CodeSample(
                """
                Input(Bind: () => _model.Name,
                      Placeholder: "Your name")
                P()[$"Hello, {_model.Name}!"]
                """,
                Notes:
                "Bind reads the expression — the property name becomes the input name, the property type picks the input type, and string fields update on every keystroke. One call replaces Value + OnInput + parsing.",
                Result: BindingTypedDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — across primitive types"],
            CodeSample(
                """
                Input(Bind: () => _model.Subscribe)   // bool   → checkbox
                Input(Bind: () => _model.Age)         // int    → number
                Input(Bind: () => _model.StartDate)   // DateOnly → date
                Select(Bind: () => _model.Favorite)[
                    Option("Red")["Red"],
                    Option("Green")["Green"],
                    Option("Blue")["Blue"]
                ]
                """,
                Notes:
                "The same Bind helper picks the right input type from the property's CLR type and wires immediate (string) or change-deferred (everything else) update timing automatically.",
                Result: BindingMultiDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — Textarea<TProp>(Bind: ...)"],
            CodeSample(
                """
                Textarea(Bind: () => _model.Notes,
                         Rows: 3,
                         Placeholder: "Jot something down…")
                Pre()[$"Notes = \"{_model.Notes}\""]
                """,
                Notes:
                "Textareas always stream — Textarea.Bound wires OnInputAsync for every keystroke so the echo updates without blur or submit, no matter how long the text is.",
                Result: BindingTextareaDemo())
        ];
}
