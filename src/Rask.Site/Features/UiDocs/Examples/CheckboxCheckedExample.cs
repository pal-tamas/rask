namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxCheckedExample : Component
{
    protected override Component? Render() =>
        Ui.Checkbox.Checked().Label("Enable notifications");
}
