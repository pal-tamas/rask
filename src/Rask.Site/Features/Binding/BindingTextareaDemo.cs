namespace Rask.Site.Features;

public sealed partial class BindingTextareaDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        UiTextarea.Bind(() => _model.Notes).AccessibleLabel("Jot something down…")
            .Id("bind-textarea")
            .Rows(3)
            .Placeholder("Jot something down…").Class("mb-2"),
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code[
                $"Notes  = \"{_model.Notes}\"\n" +
                $"Length = {_model.Notes.Length}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string Notes { get; set; } = "";
    }
}
