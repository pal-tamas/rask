namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputIconButtonsExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.Input.Of<string>().Placeholder("Search orders").IconTrailing(
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.XMark).Class("-mr-1").Aria("label", "Clear")),
            Ui.Input.Value("password").Type(InputType.Password).IconTrailing(
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.Eye).Class("-mr-1").Aria("label", "Show password"))
        ];
}
