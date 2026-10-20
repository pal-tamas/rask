using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's navlist as a C# component: each example with its own code, and its reference.</summary>
[Route("navlist")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiNavlistPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Navlist.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Navlist)
            .Lead("The navbar's items in a column: a sidebar's navigation, a settings page's sections. Flux documents "
                  + "it on its navbar page.")
            .Api(UiApi.Navlist)[
            UiDocsExample.Key("ui-navlist").Heading("Navlist").File("NavlistExample.cs").Demo(NavlistExample).Testid("ui-navlist")
                .Blurb("Rows with an icon each, the first one current."),
            UiDocsExample.Key("ui-navlist-groups").Heading("Navigation groups").File("NavlistGroupsExample.cs").Demo(NavlistGroupsExample).Testid("ui-navlist-groups")
                .Blurb("A heading over its rows."),
            UiDocsExample.Key("ui-navlist-collapsible").Heading("Collapsible groups").File("NavlistCollapsibleExample.cs").Demo(NavlistCollapsibleExample).Testid("ui-navlist-collapsible")
                .Blurb("Expandable() folds a group's rows away under its heading: open unless Expanded(false), and "
                       + "opened with a click, Enter or Space before any runtime has booted."),
            UiDocsExample.Key("ui-navlist-badges").Heading("Badges").File("NavlistBadgesExample.cs").Demo(NavlistBadgesExample).Testid("ui-navlist-badges")
                .Blurb("A count or a word at the end of a row."),
            UiNavlistBehaviour
        ];
}
