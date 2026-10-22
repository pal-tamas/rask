namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputClassTargetingExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Value("BA7K-7QZ5").Class("max-w-xs").InputClass("font-mono");
}
