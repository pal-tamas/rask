namespace Rask.Site.Features;

public sealed partial class BindingTypedDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        UiInput.Bind(() => _model.Name).AccessibleLabel("Your name")
            .Placeholder("Your name").Class("mb-2"),
        P.Class("text-sm mb-0")[
            "Hello, ",
            Strong[string.IsNullOrEmpty(_model.Name) ? "stranger" : _model.Name],
            "!"
        ]
    ];

    private sealed class Holder
    {
        public string Name { get; set; } = "";
    }
}
