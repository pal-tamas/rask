using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's heading as a C# component: its examples and its reference.</summary>
[Route("heading")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiHeadingPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Heading.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Heading)
            .Lead("A title. How big it looks and where it sits in the document's outline are two steps, so a small "
                  + "heading can still be an <h2>, and a large one can stay out of the outline.")
            .Files(["UiKitHeadingDemo.cs"])
            .Api(UiApi.Heading)
            .Demo(UiKitHeadingDemo);
}
