namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ContextExample : Component
{
    private string _sort = "name";
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Ui.Context[
                // Focusable, so the ContextMenu key and Shift+F10 open it too.
                Div.TabIndex(0)
                    .Class("inline-block rounded-lg border-2 border-dashed border-zinc-300 px-16 py-6 text-sm "
                           + "text-zinc-500 dark:border-zinc-600 dark:text-zinc-400")["Right click"],
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
                    Ui.MenuSeparator.Key("s2"),
                    Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(() => { _last = "deleted"; })["Delete"]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
