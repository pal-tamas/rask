namespace Rask.Example.Shared.Features;

// Each live demo is its own user component so the bound input's auto-registered
// handler owner resolves to *this* demo (the structural CurrentParent at handler
// registration). Without this wrapper the owner falls back to CodeSample, which
// re-renders only itself and never re-evaluates the page's state.

public sealed partial class BindingManualDemo : Component
{
    private string _typed = "";

    protected override Component? Render() =>
    [
        Input
            .Value(_typed)
            .Type(InputType.Text)
            .Class($"{Tw.Input} mb-2")
            .Placeholder("Type something")
            .OnInput(v => _typed = v),
        P.Class("text-sm mb-0")[
            "Echo: ",
            Code[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}
