using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's brand as a C# component: each example with its own code, and its reference.</summary>
[Route("brand")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiBrandPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Brand.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Brand)
            .Lead("The product's mark and name, linking home, at the head of a header or a sidebar. The mark is an "
                  + "image's address or anything you draw.")
            .Api(UiApi.Brand)[
            UiDocsExample.Key("ui-brand").Heading("Brand").File("BrandExample.cs").Demo(BrandExample).Testid("ui-brand")
                .Blurb("An image and a name, one link."),
            UiDocsExample.Key("ui-brand-slot").Heading("Logo slot").File("BrandLogoSlotExample.cs").Demo(BrandLogoSlotExample).Testid("ui-brand-slot")
                .Blurb("A mark you draw, in a box LogoClass dresses: here an icon on a cyan circle."),
            UiDocsExample.Key("ui-brand-only").Heading("Logo only").File("BrandLogoOnlyExample.cs").Demo(BrandLogoOnlyExample).Testid("ui-brand-only")
                .Blurb("No Name: the mark alone, and Alt says what it is."),
            UiDocsExample.Key("ui-brand-header").Heading("In a header").File("BrandHeaderExample.cs").Demo(BrandHeaderExample).Testid("ui-brand-header")
                .Blurb("At the start of a header, before a navbar and a profile."),
            UiBrandBehaviour
        ];
}
