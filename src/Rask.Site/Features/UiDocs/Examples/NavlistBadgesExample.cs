namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavlistBadgesExample : Component
{
    protected override Component? Render() =>
        Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Key("home").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavlistItem.Key("inbox").Href("#").Icon(Ui.IconName.Envelope).Badge("12")["Inbox"],
            Ui.NavlistItem.Key("contacts").Href("#").Icon(Ui.IconName.UserGroup)["Contacts"],
            Ui.NavlistItem.Key("calendar").Href("#").Icon(Ui.IconName.CalendarDays).Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
        ];
}
