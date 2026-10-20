using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's heading as a C# component: each example with its own code, and its reference.</summary>
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
            .Lead("A title. How big it looks and where it sits in the document's outline are two steps, so a "
                  + "small heading can still be an <h2>, and a large one can stay out of the outline.")
            .Api(UiApi.Heading)[
            UiDocsExample.Key("ui-heading-sizes").Heading("Sizes").File("HeadingSizesExample.cs").Demo(HeadingSizesExample).Testid("ui-heading-sizes")
                .Blurb("14, 16, 24 and 36 pixels."),
            UiDocsExample.Key("ui-heading-level").Heading("Heading level").File("HeadingLevelExample.cs").Demo(HeadingLevelExample).Testid("ui-heading-level")
                .Blurb("The outline level is its own step: this one is an <h3> at the base size."),
            UiDocsExample.Key("ui-heading-leading").Heading("Leading subheading").File("HeadingLeadingExample.cs").Demo(HeadingLeadingExample).Testid("ui-heading-leading")
                .Blurb("A line of text above a heading.")
        ];
}
