namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.Checkbox.Disabled().Label("Read and write");
}
