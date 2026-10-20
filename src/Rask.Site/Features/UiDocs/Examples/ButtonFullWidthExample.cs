namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonFullWidthExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Button.Primary.Class("w-full")["Send invite"]
        ];
}
