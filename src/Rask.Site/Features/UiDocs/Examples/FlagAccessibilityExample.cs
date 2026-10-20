namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagAccessibilityExample : Component
{
    // Beside the country's name a flag is decoration; on its own it says which country it is.
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Div.Key("decorative").Class("flex items-center gap-2")[Ui.Flag.Country("CA"), Ui.Text["Canada"]],
            Ui.Flag.Key("informative").Country("CA").Alt("Canada")
        ];
}
