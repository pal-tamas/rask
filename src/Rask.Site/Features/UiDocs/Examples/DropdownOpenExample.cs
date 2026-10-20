namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownOpenExample : Component
{
    private bool _open;
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown.Open(_open).OnToggle(open => { _open = open; })[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)[_open ? "Close menu" : "Open menu"],
                Ui.Menu[
                    Ui.MenuItem.Kbd("⌘R").OnClick(() => Did("rename"))["Rename"],
                    Ui.MenuItem.OnClick(() => Did("archive"))["Archive"]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];

    private void Did(string action)
    {
        _last = action;
        _open = false;
    }
}
