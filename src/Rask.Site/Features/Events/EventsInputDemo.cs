namespace Rask.Site.Features;

public sealed partial class EventsInputDemo : Component
{
    private string _typed = string.Empty;

    protected override Component? Render() =>
    [
        UiInput.Value(_typed).AccessibleLabel("Type something")
            .Type(InputType.Text)
            .Placeholder("Type something")
            .OnInput(v => _typed = v).Class("mb-2"),
        P.Class("text-sm mb-0")[
            "You typed: ",
            Code[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}
