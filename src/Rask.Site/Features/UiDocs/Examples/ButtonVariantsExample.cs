namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-3")[
            Ui.Button["Default"],
            Ui.Button.Primary["Primary"],
            Ui.Button.Filled["Filled"],
            Ui.Button.Danger["Danger"],
            Ui.Button.Ghost["Ghost"],
            Ui.Button.Subtle["Subtle"]
        ];
}
