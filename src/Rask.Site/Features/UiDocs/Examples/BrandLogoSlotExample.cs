namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BrandLogoSlotExample : Component
{
    protected override Component? Render() =>
        Ui.Brand.Href("#").Name("Launchpad")
            .Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro)
            .LogoClass("rounded-full bg-cyan-500 text-white");
}
