using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's text and link as a C# component: each example with its own code, and its reference.</summary>
[Route("text")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTextPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Text.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Text)
            .Lead("Body copy in three inks, four sizes or any Tailwind hue, and the link that sits in it. A "
                  + "link given a generated route stays inside the app; one given a URL is a link the browser "
                  + "follows.")
            .Api(UiApi.Text)[
            UiDocsExample.Key("ui-text-variants").Heading("Variants").File("TextVariantsExample.cs").Demo(TextVariantsExample).Testid("ui-text-variants")
                .Blurb("Three inks, or any Tailwind hue."),
            UiDocsExample.Key("ui-text-sizes").Heading("Size").File("TextSizesExample.cs").Demo(TextSizesExample).Testid("ui-text-sizes"),
            UiDocsExample.Key("ui-text-link").Heading("Link").File("TextLinkExample.cs").Demo(TextLinkExample).Testid("ui-text-link")
                .Blurb("External() opens a URL in a new tab, safely."),
            UiDocsExample.Key("ui-text-link-variants").Heading("Link variants").File("TextLinkVariantsExample.cs").Demo(TextLinkVariantsExample).Testid("ui-text-link-variants")
        ];
}
