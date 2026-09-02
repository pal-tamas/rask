namespace Rask.Example.Shared.Features;

public sealed partial class BindingTextareaDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Textarea.Bind(() => _model.Notes)
            .Id("bind-textarea")
            .Class($"{Tw.Input} mb-2")
            .Rows(3)
            .Placeholder("Jot something down…"),
        Pre.Class("text-sm mb-0 p-3 bg-slate-100 border rounded")[
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
