namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemAppsExample : Component
{
    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            App("Release bot", "Used 4 minutes ago"),
            App("Deploy hook", "Used yesterday"),
            App("Docs search", "Never used")
        ];

    private static Component App(string name, string used) =>
        Ui.Item[
            Ui.ItemHeading[name],
            Ui.ItemActions[
                Ui.Text[used],
                Ui.Dropdown.End[
                    Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal).Attributes(("aria-label", name + " options")),
                    Ui.Menu[Ui.MenuItem.Danger["Disconnect…"]]
                ]
            ]
        ];
}
