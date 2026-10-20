namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavlistGroupsExample : Component
{
    protected override Component? Render() =>
        Ui.Navlist.Class("w-64")[
            Ui.NavlistGroup.Heading("Account")[
                Ui.NavlistItem.Key("profile").Href("#").Current(true)["Profile"],
                Ui.NavlistItem.Key("settings").Href("#")["Settings"],
                Ui.NavlistItem.Key("billing").Href("#")["Billing"]
            ]
        ];
}
