namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioSegmentedExample : Component
{
    private string Role { get; set; } = "administrator";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.RadioGroup.Bind(() => Role).Live().Id("rd-segmented").Label("Role").Segmented[
                Ui.Radio.Value("administrator").Label("Admin"),
                Ui.Radio.Value("editor").Label("Editor"),
                Ui.Radio.Value("viewer").Label("Viewer")
            ],
            Ui.Text.Data("testid", "ui-radio-segmented-state")[$"Role: {Role}."]
        ];
}
