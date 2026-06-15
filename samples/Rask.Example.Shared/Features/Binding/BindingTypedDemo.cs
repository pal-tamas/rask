namespace Rask.Example.Shared.Features;

public sealed class BindingTypedDemo : Component
{
    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Input(
            () => _model.Name,
            Class: "form-control mb-2",
            Placeholder: "Your name"),
        P(Class: "small mb-0")[
            "Hello, ",
            Strong()[string.IsNullOrEmpty(_model.Name) ? "stranger" : _model.Name],
            "!"
        ]
    ];

    private sealed class Holder
    {
        public string Name { get; set; } = "";
    }
}
