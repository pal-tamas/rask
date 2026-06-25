namespace Rask.Example.Shared.Features;

// Each live demo is its own user component so the bound input's auto-registered
// handler owner resolves to *this* demo (the structural CurrentParent at handler
// registration). Without this wrapper the owner falls back to CodeSample, which
// re-renders only itself and never re-evaluates the page's state.

public sealed class BindingManualDemo : Component
{
    private string _typed = "";

    protected override RenderResult Render() =>
    [
        Input(
            InputType.Text,
            Class: "form-control mb-2",
            Placeholder: "Type something",
            Value: _typed,
            OnInput: v => _typed = v),
        P(Class: "small mb-0")[
            "Echo: ",
            Code()[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}
