namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownCheckboxExample : Component
{
    private bool _read = true;
    private bool _write = true;
    private bool _delete;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Permissions"],
                Ui.Menu[
                    Ui.MenuCheckbox.Value(_read).OnChange(on => { _read = on; })["Read"],
                    Ui.MenuCheckbox.Value(_write).OnChange(on => { _write = on; })["Write"],
                    Ui.MenuCheckbox.Value(_delete).OnChange(on => { _delete = on; })["Delete"]
                ]
            ],
            Ui.Text[_delete ? "May delete." : "May not delete."]
        ];
}
