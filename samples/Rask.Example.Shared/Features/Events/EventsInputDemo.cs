namespace Rask.Example.Shared.Features;

public sealed partial class EventsInputDemo : Component
{
    private string _typed = string.Empty;

    protected override Component? Render() =>
    [
        Input
            .Value(_typed)
            .Type(InputType.Text)
            .Class("form-control mb-2")
            .Placeholder("Type something")
            .OnInput(v => _typed = v),
        P.Class("small mb-0")[
            "You typed: ",
            Code[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}
