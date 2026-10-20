namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BrandLogoOnlyExample : Component
{
    protected override Component? Render() =>
        Ui.Brand.Href("#").Logo("/img/rask-mark.svg").Alt("Acme Inc.");
}
