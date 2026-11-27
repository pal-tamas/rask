using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's item and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("item")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiItemPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Item.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Item)
            .Lead("A row with a heading, a line of supporting text and a control at the end. Group items together for "
                  + "settings pages, account details, and lists of people or connections.")
            .Api(UiApi.Item)[
            UiDocsExample.Key("ui-item").Heading("Item").File("ItemExample.cs").Demo(ItemExample).Testid("ui-item")
                .Blurb("A lone item over a group of three: a switch, a menu and a button at the end of their rows."),
            UiDocsExample.Key("ui-item-single").Heading("Single item").File("ItemSingleExample.cs").Demo(ItemSingleExample).Testid("ui-item-single")
                .Blurb("A single item needs no group: on its own it draws the same surface a group would."),
            UiDocsExample.Key("ui-item-no-subheading").Heading("Without a subheading").File("ItemNoSubheadingExample.cs").Demo(ItemNoSubheadingExample).Testid("ui-item-no-subheading")
                .Blurb("One line, still as tall as a small button, so the rows stay even."),
            UiDocsExample.Key("ui-item-actions").Heading("Actions").File("ItemActionsExample.cs").Demo(ItemActionsExample).Testid("ui-item-actions")
                .Blurb("Buttons, a switch or a value at the end of the row, centred on the text beside them."),
            UiDocsExample.Key("ui-item-media").Heading("Media").File("ItemMediaExample.cs").Demo(ItemMediaExample).Testid("ui-item-media")
                .Blurb("An avatar, an icon or anything else that leads the row."),
            UiDocsExample.Key("ui-item-narrow").Heading("Narrow widths").File("ItemNarrowExample.cs").Demo(ItemNarrowExample).Testid("ui-item-narrow")
                .Blurb("Under 24rem of its own width a button with words moves under the text; a switch stays at the end. "
                       + "The item goes by its own width, so it does the same in a modal or a sidebar."),
            UiDocsExample.Key("ui-item-team").Heading("Team members").File("ItemTeamExample.cs").Demo(ItemTeamExample).Testid("ui-item-team")
                .Blurb("People on a team, each with their role. Roles you can change open a menu."),
            UiDocsExample.Key("ui-item-apps").Heading("Connected apps").File("ItemAppsExample.cs").Demo(ItemAppsExample).Testid("ui-item-apps")
                .Blurb("When each was last used, and a menu to disconnect it."),
            UiDocsExample.Key("ui-item-billing").Heading("Billing").File("ItemBillingExample.cs").Demo(ItemBillingExample).Testid("ui-item-billing"),
            UiDocsExample.Key("ui-item-teams").Heading("Teams").File("ItemTeamsExample.cs").Demo(ItemTeamsExample).Testid("ui-item-teams"),
            UiDocsExample.Key("ui-item-notifications").Heading("Notifications").File("ItemNotificationsExample.cs").Demo(ItemNotificationsExample).Testid("ui-item-notifications"),
            UiDocsExample.Key("ui-item-danger").Heading("Danger zone").File("ItemDangerExample.cs").Demo(ItemDangerExample).Testid("ui-item-danger")
                .Blurb("Actions that are hard to undo, kept together and apart from everything else."),
            UiItemBehaviour
        ];
}
