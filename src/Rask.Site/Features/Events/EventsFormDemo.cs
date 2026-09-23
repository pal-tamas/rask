using Rask.Core.Live;

namespace Rask.Site.Features;

public sealed partial class EventsFormDemo : Component
{
    private string _submitted = "(none yet)";

    // `Form` binds a model; this demo posts raw FormData, so the model is just the field it posts.
    private readonly Fields _fields = new();

    protected override Component? Render() =>
    [
        Form.Model(_fields).OnSubmit(OnSubmit).Class("mb-2")[
            Div.Class("flex items-stretch gap-2")[
                Ui.Input.Value<string>(null)
                    .AccessibleLabel("Your name")
                    .Type(InputType.Text)
                    .Name("name")
                    .Placeholder("Your name"),
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.PaperAirplane), "Send"]
            ]
        ],
        P.Class("text-sm mb-0")["Last submitted: ", Strong[_submitted]]
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
