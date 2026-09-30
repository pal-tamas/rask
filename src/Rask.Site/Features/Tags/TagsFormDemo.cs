namespace Rask.Site.Features;

public sealed partial class TagsFormDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    protected override Component? Render() => Form.Model(_fields)[
        Div.Class("mb-2")[
            Ui.Input.Of<string>().Label("Name")
                .Type(InputType.Text)
                .Id("n")
        ],
        Ui.Button.Primary.Submit["Submit"]
    ];

    public sealed class Fields
    {
        public string? Name { get; set; }
    }
}
