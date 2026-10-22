namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputInvalidExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Of<string>().Invalid().Class("max-w-sm");
}
