namespace Rask.Site.Features.UiDocs.Examples;

// This tab's layout places Ui.ConfirmLeave once. From the first thing typed, every way out of the page asks in it
// with the form's message, until a save that passed validation.
public sealed partial class ConfirmLeaveExample : Component
{
    private readonly Note _note = new();
    private string? _saved;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_note).OnSubmit(Save).Id("ucl-form").Class("flex flex-wrap items-end gap-4")
                .ConfirmLeave("Leave without saving your note?")[
                Ui.Input.Bind(() => _note.Text).Label("Note").Id("ucl-input").Class("max-w-xs"),
                Ui.Button.Primary.Submit.Id("ucl-save")["Save"]
            ],
            Ui.Text.Id("ucl-saved")[_saved is null ? "Nothing saved yet." : "Saved: " + _saved]
        ];

    private void Save(Note note) => _saved = note.Text;

    public sealed class Note
    {
        public string Text { get; set; } = "";
    }
}
