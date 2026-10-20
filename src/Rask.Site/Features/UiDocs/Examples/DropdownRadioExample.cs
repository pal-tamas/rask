namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownRadioExample : Component
{
    private string _by = "latest";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown[
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Sort by"],
                Ui.Menu[
                    Ui.MenuRadioGroup.Value(_by).OnChange(by => { _by = by; })[
                        Ui.MenuRadio.Value("latest")["Latest activity"],
                        Ui.MenuRadio.Value("created")["Date created"],
                        Ui.MenuRadio.Value("popular")["Most popular"]
                    ]
                ]
            ],
            Ui.Text[$"Ordered by {_by}."]
        ];
}
