namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputIconsExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.Input.Of<string>().Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search orders"),
            Ui.Input.Of<string>().IconTrailing(Ui.IconName.CreditCard).Placeholder("4444-4444-4444-4444"),
            Ui.Input.Of<string>().IconTrailing(Ui.IconName.Loading).Placeholder("Search transactions")
        ];
}
