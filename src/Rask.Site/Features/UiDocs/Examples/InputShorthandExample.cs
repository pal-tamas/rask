namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputShorthandExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Of<string>().Label("Username").Description("This will be publicly displayed.").Class("max-w-sm");
}
