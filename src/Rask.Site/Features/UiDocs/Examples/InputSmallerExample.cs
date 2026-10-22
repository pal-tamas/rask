namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputSmallerExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Of<string>().Sm.Placeholder("Filter by...").Class("max-w-xs");
}
