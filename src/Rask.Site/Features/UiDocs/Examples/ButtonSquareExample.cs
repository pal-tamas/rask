namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonSquareExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-3")[
            Ui.Button.Square()["..."],
            Ui.Button.Disabled()["Disabled"]
        ];
}
