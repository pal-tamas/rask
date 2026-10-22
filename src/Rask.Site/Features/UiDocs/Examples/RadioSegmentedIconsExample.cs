namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioSegmentedIconsExample : Component
{
    private string Role { get; set; } = "administrator";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Role).Segmented[
            Ui.Radio.Value("administrator").Label("Admin").Icon(Ui.IconName.Wrench),
            Ui.Radio.Value("editor").Label("Editor").Icon(Ui.IconName.PencilSquare),
            Ui.Radio.Value("viewer").Label("Viewer").Icon(Ui.IconName.Eye)
        ];
}
