namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownExample : Component
{
    private string _sort = "name";
    private bool _draft = true;
    private bool _published = true;
    private bool _archived;
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Options"],
                Ui.Menu[
                    Ui.MenuItem.Icon(Ui.IconName.Plus).OnClick(() => { _last = "new post"; })["New post"],
                    Ui.MenuSeparator.Key("s1"),
                    Ui.MenuSubmenu.Heading("Sort by")[
                        Ui.MenuRadioGroup.Value(_sort).OnChange(sort => { _sort = sort; _last = "sorted by " + sort; })[
                            Ui.MenuRadio.Value("name")["Name"],
                            Ui.MenuRadio.Value("date")["Date"],
                            Ui.MenuRadio.Value("popularity")["Popularity"]
                        ]
                    ],
                    Ui.MenuSubmenu.Heading("Filter")[
                        Ui.MenuCheckbox.Value(_draft).OnChange(on => { _draft = on; })["Draft"],
                        Ui.MenuCheckbox.Value(_published).OnChange(on => { _published = on; })["Published"],
                        Ui.MenuCheckbox.Value(_archived).OnChange(on => { _archived = on; })["Archived"]
                    ],
                    Ui.MenuSeparator.Key("s2"),
                    Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(() => { _last = "deleted"; })["Delete"]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
