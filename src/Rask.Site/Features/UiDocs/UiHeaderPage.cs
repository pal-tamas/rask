using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's header layout in C#: each demo with its own code, and the reference of its parts.</summary>
[Route("header")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiHeaderPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Header.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Header)
            .Lead("A full-width top navigation layout for your application.")
            .Api(UiApi.Header)[
            UiDocsExample.Key("ui-header").Heading("Header").File("HeaderLayoutExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.HeaderLayoutDemoPage()).Title("The header layout")).Testid("ui-header")
                .Blurb("The brand, the navbar and the account across the top. On a phone the links move into a "
                       + "sidebar that the header's toggle slides over the page."),
            UiDocsExample.Key("ui-header-inset").Heading("Inset main").File("HeaderInsetExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.HeaderInsetDemoPage()).Title("The header layout with an inset main")).Testid("ui-header-inset")
                .Blurb("Add `Inset(true)` to `Ui.Main` to place your content in a bordered panel with rounded corners. "
                       + "On large screens, content scrolls inside the panel, keeping its border and the space around "
                       + "it visible. On smaller screens, the panel becomes full width and the page scrolls normally.")
                .After([
                    "Set the background around the panel on your layout and header. Add `lg:mt-0` to the main to "
                    + "remove the margin below the header.",
                    "Add `Container(true)` to center the content inside the full-width panel. Keep the header and main "
                    + "as direct siblings so the panel can adjust to the header's height.",
                ]),
            UiDocsExample.Key("ui-header-sidebar").Heading("Secondary sidebar").File("HeaderSecondarySidebarExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.HeaderWithSidebarDemoPage()).Title("The header layout with a secondary sidebar")).Testid("ui-header-sidebar")
                .Blurb("Use a sidebar for secondary navigation."),
            UiHeaderBehaviour
        ];
}
