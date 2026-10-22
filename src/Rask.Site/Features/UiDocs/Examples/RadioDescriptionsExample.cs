namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioDescriptionsExample : Component
{
    private string Role { get; set; } = "administrator";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Role).Label("Role")[
            Ui.Radio.Value("administrator").Label("Administrator").Description("Administrator users can perform any action."),
            Ui.Radio.Value("editor").Label("Editor").Description("Editor users have the ability to read, create, and update."),
            Ui.Radio.Value("viewer").Label("Viewer")
                .Description("Viewer users only have the ability to read. Create, and update are restricted.")
        ];
}
