namespace Rask.Example.Shared.Features;

public sealed class EventsInputDemo : Component
{
    private string _typed = string.Empty;

    protected override RenderResult Render() =>
    [
        Input(
            InputType.Text,
            Class: "form-control mb-2",
            Placeholder: "Type something",
            Value: _typed,
            OnInput: v => _typed = v),
        P(Class: "small mb-0")[
            "You typed: ",
            Code()[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}
