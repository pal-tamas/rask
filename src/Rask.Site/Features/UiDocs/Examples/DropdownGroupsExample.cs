namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownGroupsExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown[
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Groups"],
            Ui.Menu[
                Ui.MenuItem["View"],
                Ui.MenuItem["Transfer"],
                Ui.MenuSeparator.Key("s1"),
                Ui.MenuItem["Publish"],
                Ui.MenuItem["Share"],
                Ui.MenuSeparator.Key("s2"),
                Ui.MenuItem.Danger["Delete"]
            ]
        ];
}
