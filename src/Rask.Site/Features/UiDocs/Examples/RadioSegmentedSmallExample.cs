namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioSegmentedSmallExample : Component
{
    private string Role { get; set; } = "administrator";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Role).Label("Role").Segmented.Sm[
            Ui.Radio.Value("administrator").Label("Admin"),
            Ui.Radio.Value("editor").Label("Editor"),
            Ui.Radio.Value("viewer").Label("Viewer")
        ];
}
