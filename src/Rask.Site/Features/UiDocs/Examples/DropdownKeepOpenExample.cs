namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownKeepOpenExample : Component
{
    private bool _draft = true;
    private bool _published = true;
    private bool _archived;
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            // On the menu: every row keeps it open.
            Ui.Dropdown.Key("menu")[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Keep open"],
                Ui.Menu.KeepOpen()[
                    Ui.MenuCheckbox.Value(_draft).OnChange(on => { _draft = on; })["Draft"],
                    Ui.MenuCheckbox.Value(_published).OnChange(on => { _published = on; })["Published"],
                    Ui.MenuCheckbox.Value(_archived).OnChange(on => { _archived = on; })["Archived"]
                ]
            ],
            // On the rows: they keep it open, and Clear still closes it.
            Ui.Dropdown.Key("rows")[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Filters"],
                Ui.Menu[
                    Ui.MenuCheckbox.Value(_draft).KeepOpen().OnChange(on => { _draft = on; })["Draft"],
                    Ui.MenuCheckbox.Value(_published).KeepOpen().OnChange(on => { _published = on; })["Published"],
                    Ui.MenuCheckbox.Value(_archived).KeepOpen().OnChange(on => { _archived = on; })["Archived"],
                    Ui.MenuSeparator,
                    Ui.MenuItem.Danger.OnClick(() =>
                    {
                        _draft = _published = _archived = false;
                        _last = "cleared the filters";
                    })["Clear"]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
