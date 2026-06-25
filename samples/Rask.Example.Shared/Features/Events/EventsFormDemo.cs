using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

public sealed class EventsFormDemo : Component
{
    private string _submitted = "(none yet)";

    protected override RenderResult Render() =>
    [
        Form(OnSubmit: OnSubmit, Class: "mb-2")[
            Div(Class: "input-group")[
                Input<string>(
                    InputType.Text,
                    "name",
                    Class: "form-control",
                    Placeholder: "Your name"),
                Button(
                    "submit",
                    Class: "btn btn-primary")[I(Class: "bi bi-send me-1"), "Send"]
            ]
        ],
        P(Class: "small mb-0")["Last submitted: ", Strong()[_submitted]]
    ];

    private void OnSubmit(FormData fd)
    {
        var name = fd.Get("name");
        _submitted = string.IsNullOrWhiteSpace(name) ? "(blank)" : name;
    }
}
