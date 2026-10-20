using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's card and its parts as C# components: its examples and its reference.</summary>
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
                  + "spacing, dividers and corners between them: Body sets how they are apart, Variant the surface, "
                  + "Size the room.")
            .Files(["UiKitCardDemo.cs"])
            .Api(UiApi.Card)
            .Demo(UiKitCardDemo);
}
