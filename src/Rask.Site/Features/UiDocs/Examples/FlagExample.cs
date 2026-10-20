namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagExample : Component
{
    private static readonly string[] Countries = ["US", "EU", "IN", "BR", "DE", "GB", "PH"];

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Countries.Select(code => Ui.Flag.Key(code).Country(code))
        ];
}
