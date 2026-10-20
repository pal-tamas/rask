namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownShortcutsExample : Component
{
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Shortcuts"],
                Ui.Menu[
                    Ui.MenuItem.Icon(Ui.IconName.PencilSquare).Kbd("⌘S").OnClick(() => { _last = "save"; })["Save"],
                    Ui.MenuItem.Icon(Ui.IconName.DocumentDuplicate).Kbd("⌘D").OnClick(() => { _last = "duplicate"; })["Duplicate"],
                    Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray).Suffix("PDF").Disabled()["Export"],
                    Ui.MenuItem.Icon(Ui.IconName.Trash).Danger.Kbd("⌘⌫").OnClick(() => { _last = "delete"; })["Delete"]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
