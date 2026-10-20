namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavlistCollapsibleExample : Component
{
    protected override Component? Render() =>
        Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Key("dashboard").Href("#").Icon(Ui.IconName.Home).Current(true)["Dashboard"],
            Ui.NavlistItem.Key("transactions").Href("#").Icon(Ui.IconName.ListBullet)["Transactions"],
            Ui.NavlistGroup.Key("account").Heading("Account").Expandable()[
                Ui.NavlistItem.Key("profile").Href("#")["Profile"],
                Ui.NavlistItem.Key("settings").Href("#")["Settings"],
                Ui.NavlistItem.Key("billing").Href("#")["Billing"]
            ],
            Ui.NavlistGroup.Key("team").Heading("Team").Expandable().Expanded(false)[
                Ui.NavlistItem.Key("members").Href("#")["Members"]
            ]
        ];
}
