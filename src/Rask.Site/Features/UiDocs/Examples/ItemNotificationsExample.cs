namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemNotificationsExample : Component
{
    private bool _comments = true;
    private bool _mentions = true;
    private bool _digest;

    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemHeading["Comments"],
                Ui.ItemSubheading["When someone comments on a board you own."],
                Ui.ItemActions[Ui.Switch.Value(_comments).OnChange(on => { _comments = on; }).Attributes(("aria-label", "Comments"))]
            ],
            Ui.Item[
                Ui.ItemHeading["Mentions"],
                Ui.ItemSubheading["When someone mentions you anywhere."],
                Ui.ItemActions[Ui.Switch.Value(_mentions).OnChange(on => { _mentions = on; }).Attributes(("aria-label", "Mentions"))]
            ],
            Ui.Item[
                Ui.ItemHeading["Weekly digest"],
                Ui.ItemSubheading["A summary of what changed, every Monday."],
                Ui.ItemActions[Ui.Switch.Value(_digest).OnChange(on => { _digest = on; }).Attributes(("aria-label", "Weekly digest"))]
            ]
        ];
}
