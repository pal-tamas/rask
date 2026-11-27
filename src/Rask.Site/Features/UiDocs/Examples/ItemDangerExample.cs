namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemDangerExample : Component
{
    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemHeading["Transfer ownership"],
                Ui.ItemSubheading["Give this team to someone else. You'll stay on as an admin."],
                Ui.ItemActions[Ui.Button.Sm["Transfer…"]]
            ],
            Ui.Item[
                Ui.ItemHeading["Delete team"],
                Ui.ItemSubheading["Its boards are deleted for everyone. This can't be undone."],
                Ui.ItemActions[Ui.Button.Sm.Red["Delete…"]]
            ]
        ];
}
