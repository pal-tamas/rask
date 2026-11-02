namespace Rask.Site.Features.UiDocs.Examples;

// A layout of your own places the dialog once, in your words: the title a question without heading: gets, and
// the two buttons.
public sealed partial class ConfirmWordsExample : Component
{
    private int _companies = 3;

    protected override Component? Render() =>
    [
        Ui.Main.Class("flex min-h-dvh items-center justify-center")[
            Div.Class("flex flex-wrap items-center gap-4")[
                Ui.Button.Danger.Id("uc-words-delete")
                    .OnClick(() => _companies--)
                    .Confirm("Biztosan törli a céget?", heading: "Cég törlése")["Törlés"],
                Ui.Button.Id("uc-words-archive")
                    .OnClick(() => _companies--)
                    .Confirm("Archiválja a céget?")["Archiválás"],
                Ui.Text.Id("uc-words-left")[$"{_companies} cég"]
            ]
        ],
        Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
    ];
}
