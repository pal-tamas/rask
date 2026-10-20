namespace Rask.Site.Features.UiDocs;

/// <summary>How a profile behaves: a trigger and nothing more, and what it draws without a picture.</summary>
internal sealed partial class UiProfileBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("trigger").Heading("A trigger, and nothing more").Paragraphs([
                "A profile is a `<button>`. It is the trigger of an account menu or a profile switcher, and the menu "
                + "is the dropdown's: `Ui.Dropdown[Ui.Profile.Name(\"Ada\"), Ui.Menu[…]]`, or a `Ui.Navmenu` for a "
                + "menu of links. At the foot of a sidebar the same button is `Ui.SidebarProfile`, a part of the "
                + "sidebar layout.",
            ]),
            UiDocsTopic.Key("initials").Heading("Without a picture").Paragraphs([
                "`Avatar` is an image's address, or an avatar of your own. Without one the profile draws the initials "
                + "of `Name`, or of `AvatarName` for a profile that shows no name, since most accounts have no "
                + "picture. `Initials` states them, and `AvatarColor` fills the circle or square behind them.",
            ])
        ];
}
