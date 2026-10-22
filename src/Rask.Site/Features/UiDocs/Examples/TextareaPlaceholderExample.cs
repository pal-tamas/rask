namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextareaPlaceholderExample : Component
{
    private string _notes = "";

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Textarea.Value(_notes).Label("Order notes").Placeholder("No lettuce, tomato, or onion...")
                .OnChange(notes => { _notes = notes; }),
            Ui.Text.Data("testid", "ui-textarea-state")[_notes.Length == 0 ? "No notes yet." : $"Notes: {_notes}"]
        ];
}
