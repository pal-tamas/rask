using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

public sealed partial class EventsFormDemo : Component
{
    private string _submitted = "(none yet)";

    // `Form` binds a model; this demo posts raw FormData, so the model is just the field it posts.
    private readonly Fields _fields = new();

    protected override Component? Render() =>
    [
        Form.Model(_fields).OnSubmit(OnSubmit).Class("mb-2")[
            Div.Class("input-group")[
                Input.Value<string>(null)
                    .Type(InputType.Text)
                    .Name("name")
                    .Class("form-control")
                    .Placeholder("Your name"),
                Button.Class(Ui.BtnPrimary).Type("submit")[Icon.Name(IconName.Send).Class("me-1"), "Send"]
            ]
        ],
        P.Class("small mb-0")["Last submitted: ", Strong[_submitted]]
    ];

    private void OnSubmit(FormData fd)
    {
        var name = fd.Get("name");
        _submitted = string.IsNullOrWhiteSpace(name) ? "(blank)" : name;
    }

    private sealed class Fields
    {
        public string? Name { get; set; }
    }
}
