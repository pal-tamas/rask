namespace Rask.Example.Shared.Features;

public sealed partial class TagsFormDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    protected override Component? Render() => Form.Model(_fields)[
        Div.Class("mb-2")[
            Label.For("n").Class($"{Tw.Label} text-sm mb-1")["Name"],
            Input.Value<string>(null)
                .Type(InputType.Text)
                .Id("n")
                .Class(Tw.Input)
                .Placeholder("Jane Doe")
        ],
        Button.Class(Tw.BtnPrimary).Type("submit")["Submit"]
    ];

    private sealed class Fields
    {
        public string? Name { get; set; }
    }
}
