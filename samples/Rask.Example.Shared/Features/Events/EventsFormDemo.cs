using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

public sealed partial class EventsFormDemo : Component
{
    private string _submitted = "(none yet)";

    protected override Component? Render() =>
    [
        Form(OnSubmit: OnSubmit, Class: "mb-2")[
            Div(Class: "input-group")[
                Rask.Core.Components.Generated.Input<string>(
                    InputType.Text,
                    "name",
                    Class: "form-control",
                    Placeholder: "Your name"),
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Send, Class: "me-1"), "Send"]
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
