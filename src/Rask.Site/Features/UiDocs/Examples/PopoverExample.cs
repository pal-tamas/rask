namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverExample : Component
{
    private string _sort = "posted";
    private string _view = "list";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Dropdown[
                Ui.Button.Icon(Ui.IconName.AdjustmentsHorizontal).IconTrailing(Ui.IconName.ChevronDown)["Options"],
                Ui.Popover.Class("flex flex-col gap-4")[
                    Ui.RadioGroup.Value(_sort).Id("popover-sort").Label("Sort by").OnChange(v => { _sort = v; })[
                        Ui.Radio.Value("active").Label("Recently active"),
                        Ui.Radio.Value("posted").Label("Date posted")
                    ],
                    Ui.Separator.Key("s1").Subtle,
                    Ui.RadioGroup.Value(_view).Id("popover-view").Label("View as").OnChange(v => { _view = v; })[
                        Ui.Radio.Value("list").Label("List"),
                        Ui.Radio.Value("gallery").Label("Gallery")
                    ],
                    Ui.Separator.Key("s2").Subtle,
                    Ui.Button.Subtle.Sm.Class("justify-start -m-2 px-2!")
                        .OnClick(() => { _sort = "posted"; _view = "list"; })["Reset to default"]
                ]
            ],
            Ui.Text.Data("testid", "ui-popover-says")[$"Sorted by {_sort}, viewed as {_view}."]
        ];
}
