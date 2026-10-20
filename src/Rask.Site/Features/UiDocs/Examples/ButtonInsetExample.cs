namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonInsetExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex max-w-xs items-center justify-between")[
            Ui.Heading["Post successfully created."],
            Ui.Button.Ghost.Sm.Icon(Ui.IconName.XMark).Inset(Ui.Inset.All).AriaLabel("Dismiss")
        ];
}
