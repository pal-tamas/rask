namespace Rask.Example.Shared.Features;

public sealed class TagsFormDemo : Component
{
    protected override RenderResult Render() => Form()[
        Div(Class: "mb-2")[
            Label("n", Class: "form-label small mb-1")["Name"],
            Input<string>("text", Id: "n", Class: "form-control form-control-sm", Placeholder: "Jane Doe")
        ],
        Button("submit", Class: "btn btn-primary btn-sm")["Submit"]
    ];
}
