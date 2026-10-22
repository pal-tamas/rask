namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxButtonsExample : Component
{
    private ICollection<string> Features { get; set; } = ["notifications", "analytics"];

    protected override Component? Render() =>
        Ui.CheckboxGroup.Bind(() => Features).Label("Features").Buttons.Class("flex-wrap")[
            Ui.Checkbox.Value("notifications").Icon(Ui.IconName.Bell).Label("Notifications"),
            Ui.Checkbox.Value("analytics").Icon(Ui.IconName.ChartBar).Label("Analytics"),
            Ui.Checkbox.Value("backups").Icon(Ui.IconName.CloudArrowUp).Label("Backups")
        ];
}
