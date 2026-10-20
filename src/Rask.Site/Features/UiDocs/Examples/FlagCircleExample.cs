namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagCircleExample : Component
{
    private static readonly string[] Countries = ["US", "FR", "JP", "BR", "NG"];

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Countries.Select(code => Ui.Flag.Key(code).Circle().Country(code))
        ];
}
