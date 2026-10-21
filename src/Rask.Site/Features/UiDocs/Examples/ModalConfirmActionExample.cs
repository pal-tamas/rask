namespace Rask.Site.Features.UiDocs.Examples;

// The layout places the dialog once — Ui.Confirm, or Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen") —
// and every .Confirm("…") on every page asks in it.
public sealed partial class ModalConfirmActionExample : Component
{
    private string _project = "Rask";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.Danger.Id("ui-confirm-delete")
                .OnClick(() => _project = "")
                .Confirm("You're about to delete this project. This action cannot be reversed.")["Delete"],
            Ui.Button.Id("ui-confirm-delete-by-name")
                .OnClick(() => _project = "")
                .Confirm("Type the project's name, Rask, to delete it.", expect: "Rask")["Delete by name"],
            Ui.Button.Ghost.Id("ui-confirm-restore").OnClick(() => _project = "Rask")["Restore"],
            Ui.Text.Id("ui-confirm-state")[_project.Length > 0 ? $"The {_project} project is still there." : "You deleted the project."]
        ];
}
