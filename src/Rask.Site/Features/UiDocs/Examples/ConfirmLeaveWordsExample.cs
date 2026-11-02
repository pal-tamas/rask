namespace Rask.Site.Features.UiDocs.Examples;

// A layout of your own places the dialog once, in your words: a short title over the form's question, and the
// two buttons.
public sealed partial class ConfirmLeaveWordsExample : Component
{
    private readonly Note _note = new();

    protected override Component? Render() =>
    [
        Ui.Main.Class("flex min-h-dvh items-center justify-center")[
            Form.Model(_note).OnSubmit(_ => { }).Id("ucl-words-form").Class("flex flex-wrap items-end gap-4")
                .ConfirmLeave("A jegyzet még nincs mentve. Elhagyja az oldalt?")[
                Ui.Input.Bind(() => _note.Text).Label("Jegyzet").Id("ucl-words-input").Class("max-w-xs"),
                Ui.Button.Primary.Submit["Mentés"],
                Ui.Link.Href(PageMeta.LinkTo(Routes.ConfirmWordsDemoPage())).Id("ucl-words-away")["Tovább"]
            ]
        ],
        Ui.ConfirmLeave.Heading("Nem mentett változások").Stay("Nem").Leave("Igen")
    ];

    public sealed class Note
    {
        public string Text { get; set; } = "";
    }
}
