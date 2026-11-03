namespace Rask.Site.Features.UiDocs.Examples;

// This tab's layout places Ui.Confirm once, so every .Confirm("…") on its pages asks in that dialog. heading: is
// this question's own title, and the question is the sentence beneath it.
public sealed partial class ConfirmExample : Component
{
    private string _project = "Rask";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.Danger.Id("uc-delete")
                .OnClick(() => _project = "")
                .Confirm("You're about to delete this project. This action cannot be reversed.", heading: "Delete project?")["Delete project"],
            Ui.Button.Ghost.Id("uc-restore").OnClick(() => _project = "Rask")["Restore"],
            Ui.Text.Id("uc-state")[_project.Length > 0 ? $"The {_project} project is still there." : "You deleted the project."]
        ];
}
