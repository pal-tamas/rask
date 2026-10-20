using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's card and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("card")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCardPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Card.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Card)
            .Lead("A container for related content. Give it a header, a body and a footer and it handles the "
                  + "spacing, dividers and corners between them: Body sets how they are apart, Variant the "
                  + "surface, Size the room.")
            .Api(UiApi.Card)[
            UiDocsExample.Key("ui-card-intro").Heading("Card").File("CardExample.cs").Demo(CardExample).Testid("ui-card-intro")
                .Blurb("Anything put straight inside is padded evenly."),
            UiDocsExample.Key("ui-card-profile").Heading("Header, body and footer").File("CardProfileExample.cs").Demo(CardProfileExample).Testid("ui-card-profile"),
            UiDocsExample.Key("ui-card-link").Heading("As a link").File("CardLinkExample.cs").Demo(CardLinkExample).Testid("ui-card-link")
                .Blurb("The link goes around a small card, and the hover is the call site's."),
            UiDocsExample.Key("ui-card-variants").Heading("Variants").File("CardVariantsExample.cs").Demo(CardVariantsExample).Testid("ui-card-variants")
                .Blurb("The surface, from raised to a bare edge."),
            UiDocsExample.Key("ui-card-sizes").Heading("Sizes").File("CardSizesExample.cs").Demo(CardSizesExample).Testid("ui-card-sizes")
                .Blurb("The room: padding, corners and the space between the parts."),
            UiDocsExample.Key("ui-card-body").Heading("Body").File("CardBodyExample.cs").Demo(CardBodyExample).Testid("ui-card-body")
                .Blurb("How the header, the body and the footer are set apart."),
            UiDocsExample.Key("ui-card-bleed").Heading("Bleed").File("CardBleedExample.cs").Demo(CardBleedExample).Testid("ui-card-bleed")
                .Blurb("Media that runs to the card's edges."),
            UiDocsExample.Key("ui-card-outside").Heading("Header outside").File("CardOutsideHeaderExample.cs").Demo(CardOutsideHeaderExample).Testid("ui-card-outside")
                .Blurb("A header beside the card rather than in it."),
            UiDocsExample.Key("ui-card-nested").Heading("Header inside the body").File("CardNestedHeaderExample.cs").Demo(CardNestedHeaderExample).Testid("ui-card-nested")
        ];
}
