namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioPillsExample : Component
{
    private string Priority { get; set; } = "medium";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Priority).Label("Priority").Pills[
            Ui.Radio.Value("low").Label("Low"),
            Ui.Radio.Value("medium").Label("Medium"),
            Ui.Radio.Value("high").Label("High"),
            Ui.Radio.Value("critical").Label("Critical")
        ];
}
