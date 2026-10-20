using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's navbar as a C# component: each example with its own code, and its reference.</summary>
[Route("navbar")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiNavbarPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Navbar.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Navbar)
            .Lead("A header's links in a row. The current one is inked and underlined in the accent, worked out from "
                  + "the route; an icon leads, a badge trails, and an item with nowhere to go opens a menu.")
            .Api(UiApi.Navbar)[
            UiDocsExample.Key("ui-navbar").Heading("Navbar").File("NavbarExample.cs").Demo(NavbarExample).Testid("ui-navbar")
                .Blurb("Links in a row, the first one current."),
            UiDocsExample.Key("ui-navbar-icons").Heading("With icons").File("NavbarIconsExample.cs").Demo(NavbarIconsExample).Testid("ui-navbar-icons")
                .Blurb("An icon before each item's words."),
            UiDocsExample.Key("ui-navbar-badges").Heading("With badges").File("NavbarBadgesExample.cs").Demo(NavbarBadgesExample).Testid("ui-navbar-badges")
                .Blurb("A count or a word after the words, in any colour."),
            UiDocsExample.Key("ui-navbar-dropdown").Heading("Dropdown navigation").File("NavbarDropdownExample.cs").Demo(NavbarDropdownExample).Testid("ui-navbar-dropdown")
                .Blurb("An item with no Href is a button, and a dropdown makes it open a menu of links. The current "
                       + "item here is drawn in the page's ink: Accent(false)."),
            UiNavbarBehaviour
        ];
}
