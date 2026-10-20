namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Ui.Flag.Key("xl").Country("US").Xl,
            Ui.Flag.Key("lg").Country("US").Lg,
            Ui.Flag.Key("md").Country("US").Md,
            Ui.Flag.Key("sm").Country("US"),
            Ui.Flag.Key("xs").Country("US").Xs
        ];
}
