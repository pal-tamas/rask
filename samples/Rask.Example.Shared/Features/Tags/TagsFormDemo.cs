namespace Rask.Example.Shared.Features;

public sealed partial class TagsFormDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    protected override Component? Render() => Form.Model(_fields)[
        Div.Class("mb-2")[
            Label.For("n").Class("form-label small mb-1")["Name"],
            Input.Value<string>(null)
                .Type(InputType.Text)
                .Id("n")
                .Class("form-control form-control-sm")
                .Placeholder("Jane Doe")
        ],
        BsButton.Type("submit").Color(BsColor.Primary).Size(BsSize.Sm)["Submit"]
    ];

    private sealed class Fields
    {
        public string? Name { get; set; }
    }
}
