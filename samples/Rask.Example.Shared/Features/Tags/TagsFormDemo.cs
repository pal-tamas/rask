namespace Rask.Example.Shared.Features;

public sealed partial class TagsFormDemo : Component
{
    protected override Component? Render() => Form()[
        Div(Class: "mb-2")[
            Label("n", Class: "form-label small mb-1")["Name"],
            Rask.Core.Components.Generated.Input<string>(InputType.Text, Id: "n", Class: "form-control form-control-sm", Placeholder: "Jane Doe")
        ],
        BsButton(Type: "submit", Color: BsColor.Primary, Size: BsSize.Sm)["Submit"]
    ];
}
