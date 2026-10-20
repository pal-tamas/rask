namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BrandExample : Component
{
    protected override Component? Render() =>
        Ui.Brand.Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc.");
}
