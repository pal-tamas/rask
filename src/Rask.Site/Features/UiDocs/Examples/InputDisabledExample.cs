namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Of<string>().Disabled().Label("Email").Class("max-w-sm");
}
