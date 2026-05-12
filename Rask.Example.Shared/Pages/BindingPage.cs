using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("binding")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BindingPage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Two-way binding",
                "Inputs can be wired with plain Value + OnInput, or with a strongly-typed Bind expression that resolves the input name, type, and update timing for you."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Manual — Value + OnInput"]),
            Components.CodeSample(
                """
                Input(Type: "text",
                      Value: _typed,
                      OnInput: v => _typed = v)
                P(Children: [$"Echo: {_typed}"])
                """,
                Notes: "The low-level path: wire Value and the event handler yourself. Works for any input type, but you parse and re-render manually.",
                Result: Components.BindingManualDemo()),
            H2(Class: "h4 mt-5 mb-3", Children: ["Typed — Input<TProp>(Bind: ...)"]),
            Components.CodeSample(
                """
                Input(Bind: () => _model.Name,
                      Placeholder: "Your name")
                P(Children: [$"Hello, {_model.Name}!"])
                """,
                Notes: "Bind reads the expression — the property name becomes the input name, the property type picks the input type, and string fields update on every keystroke. One call replaces Value + OnInput + parsing.",
                Result: Components.BindingTypedDemo()),
            H2(Class: "h4 mt-5 mb-3", Children: ["Typed — across primitive types"]),
            Components.CodeSample(
                """
                Input(Bind: () => _model.Subscribe)   // bool   → checkbox
                Input(Bind: () => _model.Age)         // int    → number
                Input(Bind: () => _model.StartDate)   // DateOnly → date
                Select(Bind: () => _model.Favorite, Children: [
                    Option("Red",   Children: ["Red"]),
                    Option("Green", Children: ["Green"]),
                    Option("Blue",  Children: ["Blue"])
                ])
                """,
                Notes: "The same Bind helper picks the right input type from the property's CLR type and wires immediate (string) or change-deferred (everything else) update timing automatically.",
                Result: Components.BindingMultiDemo())
        );
}
