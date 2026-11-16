namespace Rask.Site.Features.UiDocs.Examples;

// A link asks too: .SignOut() asks the catalog's question under its title, and a yes follows the link — your sign-out
// endpoint, a GET and a page load. Here it only moves to #signed-out, so the page stays where it is.
public sealed partial class ConfirmSignOutExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown[
            Ui.Button.Id("uc-account").IconTrailing(Ui.IconName.ChevronDown)["Rosa Vidal"],
            Ui.Menu[
                Ui.MenuItem.Icon(Ui.IconName.Cog6Tooth).Href("#settings")["Settings"],
                Ui.MenuSeparator,
                Ui.MenuItem.Href("#signed-out").SignOut()
            ]
        ];
}
